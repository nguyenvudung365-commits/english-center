using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// TV2: bài tập theo lớp, công bố + snapshot nghĩa vụ, chấm/sửa điểm có lịch sử, xem điểm, luyện tập, missed/replay.
public sealed class Tv2CourseworkTests : IAsyncLifetime
{
    private const string Password = "Coursework-test-123!";
    private static readonly string[] Students = ["student", "other-student", "revoked-student", "late-student"];
    private readonly CourseworkFactory factory = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, Guid> users = [];
    private Guid classId;
    private Guid otherClassId;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var academic = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await identity.Database.EnsureCreatedAsync();
        await academic.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>().Database.EnsureCreatedAsync();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var accounts = new[] { ("admin", Roles.Admin), ("staff", Roles.Staff), ("teacher", Roles.Teacher), ("other-teacher", Roles.Teacher) }
            .Concat(Students.Select(x => (x, Roles.Student)));
        foreach (var (name, role) in accounts)
        {
            var user = User.Create($"{name}@example.test", name, role, passwords.Hash(Password), DateTimeOffset.UtcNow);
            identity.Users.Add(user);
            users[name] = user.Id;
            if (role == Roles.Teacher) academic.Teachers.Add(Teacher.Create(user.Id, $"GV {name}", user.Email));
        }
        await identity.SaveChangesAsync();
        var course = Course.Create(Guid.NewGuid(), "TV2", "Coursework");
        academic.Courses.Add(course);
        var item = Class.Create("TV2-01", "Lớp TV2", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 12, 31), 20, 0,
            users["teacher"], DateTimeOffset.UtcNow);
        var other = Class.Create("TV2-02", "Lớp khác", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 12, 31), 20, 0,
            users["other-teacher"], DateTimeOffset.UtcNow);
        academic.Classes.AddRange(item, other);
        classId = item.Id; otherClassId = other.Id;
        await academic.SaveChangesAsync();
        var enrollment = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        foreach (var name in Students) enrollment.Students.Add(new Student(users[name], name, $"{name}@example.test"));
        enrollment.Classes.Add(new ClassSnapshot(item.Id, item.Code, item.StartDate, item.EndDate, item.MaxStudents, item.TuitionFee));
        enrollment.Classes.Add(new ClassSnapshot(other.Id, other.Code, other.StartDate, other.EndDate, other.MaxStudents, other.TuitionFee));
        await enrollment.SaveChangesAsync();
    }

    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }

    private async Task As(string name)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new { email = $"{name}@example.test", password = Password });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    // Ghi danh thật (registration → StudentEnrolled → roster Academic) rồi bật quyền coursework. Trả membership id.
    private async Task<Guid> Enroll(string student = "student", Guid? inClass = null)
    {
        var target = inClass ?? classId;
        await As("admin");
        var registered = await client.PostAsJsonAsync("/api/enrollment/enrollments", new
        {
            classId = target, studentId = users[student], maxAbsences = 3, maxLates = 5, maxSkippedAssignments = 2
        });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var response = await client.PostAsJsonAsync("/api/enrollment/memberships", new { classId = target, studentId = users[student] });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task Revoke(Guid membership)
    {
        await As("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/enrollment/memberships/{membership}")).StatusCode);
    }

    private async Task<JsonElement> CreateAssignment(string title = "Bài tập", bool? publish = null, string? kind = null,
        string teacher = "teacher", int closesInHours = 2)
    {
        await As(teacher);
        var now = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync("/api/academic/assignments", new
        {
            classId, title, description = "Mô tả", opensAtUtc = now.AddHours(-1), closesAtUtc = now.AddHours(closesInHours),
            kind, unit = "Unit 3", lesson = "Lesson 2", publish
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private async Task<Guid> File(string owner = "student")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>();
        var file = StoredFile.Create(users[owner], $"{Guid.NewGuid():N}.pdf", "application/pdf", 1024,
            "submission", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        file.Complete($"files/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<Guid> Submit(Guid assignment, string student = "student")
    {
        var file = await File(student);
        await As(student);
        var response = await client.PutAsJsonAsync($"/api/academic/assignments/{assignment}/submissions", new { fileIds = new[] { file } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Grade(Guid submission, decimal? score, int? expected, string? feedback = null) =>
        client.PutAsJsonAsync($"/api/academic/submissions/{submission}/grade", new { score, feedback, expectedGradeVersion = expected });

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    private async Task<ReconcileResult> Reconcile(TimeSpan shift, bool resolve)
    {
        using var scope = factory.Services.CreateScope();
        var reconciler = ActivatorUtilities.CreateInstance<CourseworkReconciler>(scope.ServiceProvider, new ShiftedClock(shift));
        return await reconciler.RunOnceAsync(resolve, CancellationToken.None);
    }

    [Fact]
    public async Task ClassTeacher_GradesAndRegrades_WithHistoryVersionedEventsAndSafeRetry()
    {
        await Enroll();
        var assignment = (await CreateAssignment()).GetProperty("id").GetGuid();
        var submission = await Submit(assignment);
        await As("teacher");
        var first = await Grade(submission, 8.5m, 0, "Khá tốt");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var graded = await Json(first);
        Assert.Equal((8.5m, 1, "Khá tốt"), (graded.GetProperty("score").GetDecimal(), graded.GetProperty("gradeVersion").GetInt32(),
            graded.GetProperty("feedback").GetString()));
        Assert.Equal(users["teacher"], graded.GetProperty("gradedBy").GetGuid());

        Assert.Equal(HttpStatusCode.Conflict, (await Grade(submission, 9m, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 9m, 1, "Sửa sau phúc khảo")).StatusCode);
        // Retry đúng nội dung (mất response) không tạo lịch sử/event mới.
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 9m, 1, "Sửa sau phúc khảo")).StatusCode);

        var history = (await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/submissions/{submission}/grades"))!;
        Assert.Equal(2, history.Length);
        Assert.Equal((2, 9m, 8.5m), (history[0].GetProperty("version").GetInt32(), history[0].GetProperty("score").GetDecimal(),
            history[0].GetProperty("previousScore").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, history[1].GetProperty("previousScore").ValueKind);
        Assert.Equal("GV teacher", history[0].GetProperty("gradedByName").GetString());

        var events = factory.Graded.Where(x => x.SubmissionId == submission).OrderBy(x => x.GradeVersion).ToList();
        Assert.Equal(new[] { 1, 2 }, events.Select(x => x.GradeVersion));
        Assert.Equal(new[] { 8.5m, 9m }, events.Select(x => x.Score));
        Assert.All(events, x => Assert.Equal(classId, x.ClassId));
        Assert.Equal(2, events.Select(x => x.EventId).Distinct().Count());

        await As("student");
        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/{assignment}/submissions/mine");
        Assert.Equal(9m, mine.GetProperty("score").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/academic/submissions/{submission}/grades")).StatusCode);
        // Bài đã chấm không được thay file.
        var file = await File();
        await As("student");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/academic/assignments/{assignment}/submissions",
            new { fileIds = new[] { file }, expectedRevision = mine.GetProperty("revision").GetGuid() })).StatusCode);
    }

    [Theory]
    [InlineData("10.05")]
    [InlineData("-1")]
    [InlineData("10.1")]
    [InlineData("null")]
    [InlineData("no-version")]
    [InlineData("long-feedback")]
    public async Task InvalidGrade_IsRejectedWithoutHistory(string kind)
    {
        await Enroll();
        var assignment = (await CreateAssignment()).GetProperty("id").GetGuid();
        var submission = await Submit(assignment);
        await As("teacher");
        var response = kind switch
        {
            "null" => await Grade(submission, null, 0),
            "no-version" => await Grade(submission, 5m, null),
            "long-feedback" => await Grade(submission, 5m, 0, new string('x', 2001)),
            _ => await Grade(submission, decimal.Parse(kind, System.Globalization.CultureInfo.InvariantCulture), 0)
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/submissions/{submission}/grades"))!);
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 0m, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 10m, 1)).StatusCode);
    }

    [Theory]
    [InlineData("other-teacher")]
    [InlineData("admin")]
    [InlineData("staff")]
    [InlineData("student")]
    public async Task OnlyTheClassTeacher_CanGrade(string actor)
    {
        await Enroll();
        var assignment = (await CreateAssignment()).GetProperty("id").GetGuid();
        var submission = await Submit(assignment);
        await As(actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await Grade(submission, 7m, 0)).StatusCode);
        await As("teacher");
        var current = await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments/{assignment}/submissions");
        Assert.Equal(0, Assert.Single(current!).GetProperty("gradeVersion").GetInt32());
    }

    [Fact]
    public async Task GradeHistoryAndScores_AreHiddenFromOtherStudentsAndOtherTeachers_AndLockedTeacherCannotGrade()
    {
        await Enroll(); await Enroll("other-student");
        var assignment = (await CreateAssignment()).GetProperty("id").GetGuid();
        var submission = await Submit(assignment);
        await As("teacher");
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 6m, 0)).StatusCode);
        var path = $"/api/academic/submissions/{submission}/grades";
        await As("other-student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        await As("other-teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        await As("staff");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        await As("teacher");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.SingleAsync(x => x.Id == users["teacher"])).SetLocked(true);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Grade(submission, 7m, 1)).StatusCode);
    }

    [Fact]
    public async Task DraftAssignment_IsHiddenFromStudents_UntilPublishedWithRosterSnapshot()
    {
        await Enroll();
        var draft = await CreateAssignment("Nháp", publish: false, kind: "InClass");
        var id = draft.GetProperty("id").GetGuid();
        Assert.Equal(("Draft", "InClass", "Unit 3"), (draft.GetProperty("status").GetString(), draft.GetProperty("kind").GetString(),
            draft.GetProperty("unit").GetString()));
        Assert.DoesNotContain(factory.Published, x => x.AssignmentId == id);

        await As("student");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/assignments/{id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments?classId={classId}"))!);
        var file = await File(); await As("student");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}/submissions",
            new { fileIds = new[] { file } })).StatusCode);

        await As("teacher");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/academic/assignments/{id}/publish",
            new { expectedVersion = 99 })).StatusCode);
        var published = await client.PostAsJsonAsync($"/api/academic/assignments/{id}/publish",
            new { expectedVersion = draft.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal("Open", (await Json(published)).GetProperty("status").GetString());
        // Retry không tạo event/snapshot mới; body rỗng cũng được.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/academic/assignments/{id}/publish", null)).StatusCode);
        var announced = Assert.Single(factory.Published, x => x.AssignmentId == id);
        Assert.Equal((1, "InClass", classId), (announced.RecipientCount, announced.Kind, announced.ClassId));
        Assert.Equal(CourseworkEvents.Key("assignment-published", id), announced.EventId);

        using (var scope = factory.Services.CreateScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IAssignmentDirectory>();
            Assert.Equal(new[] { users["student"] }, await directory.RecipientsAsync(id, CancellationToken.None));
            Assert.NotNull(await directory.FindAsync(id, CancellationToken.None));
        }
        await As("student");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/academic/assignments/{id}")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments?classId={classId}"))!);
    }

    [Fact]
    public async Task Update_ProtectsPublishedWindowAndRejectsStaleOrInvalidInput()
    {
        var created = await CreateAssignment("Bài 1");
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt32();
        var opens = created.GetProperty("opensAtUtc").GetDateTimeOffset();
        var closes = created.GetProperty("closesAtUtc").GetDateTimeOffset();
        object Body(string title, DateTimeOffset end, int? expected, string? kind = null, Guid? sessionId = null) =>
            new { title, description = "", opensAtUtc = opens, closesAtUtc = end, kind, unit = "Unit 4", lesson = (string?)null, sessionId, expectedVersion = expected };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("Bài 1", closes, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("Bài 1", closes, version, "Quiz"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("Bài 1", closes.AddDays(1), version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("Bài 1", closes, version - 1))).StatusCode);
        // Chưa có module Session của TV1: không nhận SessionId thay vì lưu tham chiếu chưa kiểm chứng.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("Bài 1", closes, version, sessionId: Guid.NewGuid()))).StatusCode);
        var ok = await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("Bài 1 (đã sửa)", closes, version));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var updated = await Json(ok);
        Assert.Equal(("Bài 1 (đã sửa)", "Unit 4", version + 1), (updated.GetProperty("title").GetString(), updated.GetProperty("unit").GetString(),
            updated.GetProperty("version").GetInt32()));
        await As("other-teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}", Body("x", closes, version + 1))).StatusCode);
    }

    [Fact]
    public async Task StudentAssignmentList_FiltersSearchesPagesAndShowsOwnScore()
    {
        await Enroll();
        var homework = (await CreateAssignment("Writing task", kind: "Homework")).GetProperty("id").GetGuid();
        var inClass = (await CreateAssignment("Speaking drill", kind: "InClass")).GetProperty("id").GetGuid();
        await CreateAssignment("Nháp ẩn", publish: false);
        var submission = await Submit(homework);
        await As("teacher");
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 7.5m, 0, "Cần thêm ví dụ")).StatusCode);

        await As("student");
        var all = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine");
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        var graded = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine?submissionStatus=graded");
        var item = Assert.Single(graded.GetProperty("items").EnumerateArray());
        Assert.Equal((homework, 7.5m, "Cần thêm ví dụ", "Graded"), (item.GetProperty("assignment").GetProperty("id").GetGuid(),
            item.GetProperty("score").GetDecimal(), item.GetProperty("feedback").GetString(), item.GetProperty("submissionStatus").GetString()));
        Assert.True(item.GetProperty("isObligated").GetBoolean());
        var searched = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine?search=speaking&kind=InClass");
        Assert.Equal(inClass, Assert.Single(searched.GetProperty("items").EnumerateArray()).GetProperty("assignment").GetProperty("id").GetGuid());
        var notSubmitted = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine?submissionStatus=NotSubmitted");
        Assert.Equal(1, notSubmitted.GetProperty("total").GetInt32());
        var paged = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/mine?classId={classId}&page=2&pageSize=1");
        Assert.Equal((2, 1, 2), (paged.GetProperty("total").GetInt32(), paged.GetProperty("items").GetArrayLength(), paged.GetProperty("page").GetInt32()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/academic/assignments/mine?status=Draft")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/academic/assignments/mine?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments/mine?classId={otherClassId}")).StatusCode);

        await As("teacher");
        var ungraded = await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments/{homework}/submissions?graded=false");
        Assert.Empty(ungraded!);
        var drafts = await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments?classId={classId}&status=Draft");
        Assert.Equal("Nháp ẩn", Assert.Single(drafts!).GetProperty("title").GetString());
    }

    [Fact]
    public async Task MissedReconciliation_FollowsPublishSnapshot_ExemptsRevoked_AndFeedsEnrollmentOnce()
    {
        await Enroll(); await Enroll("other-student");
        var revoked = await Enroll("revoked-student");
        var assignment = (await CreateAssignment("Có hạn")).GetProperty("id").GetGuid();
        await Enroll("late-student"); // vào lớp sau công bố: không có nghĩa vụ
        await Submit(assignment);
        await Submit(assignment, "late-student");
        await Revoke(revoked);

        // Trước hạn: không chốt gì.
        Assert.Equal(0, (await Reconcile(TimeSpan.Zero, resolve: true)).AssignmentsResolved);
        var result = await Reconcile(TimeSpan.FromHours(3), resolve: true);
        Assert.Equal((1, 1, 1, 1), (result.AssignmentsResolved, result.Missed, result.Exempted, result.MissedSent));
        // Chạy lại (restart/replay) không phát thêm.
        Assert.Equal(0, (await Reconcile(TimeSpan.FromHours(4), resolve: true)).MissedSent);

        var missed = Assert.Single(factory.Missed);
        Assert.Equal((assignment, users["other-student"], classId), (missed.AssignmentId, missed.StudentId, missed.ClassId));
        Assert.Equal(missed.DueAtUtc, missed.OccurredOnUtc);
        Assert.Equal(DateTimeKind.Utc, missed.OccurredOnUtc.Kind);

        await As("teacher");
        var recipients = (await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments/{assignment}/recipients"))!
            .ToDictionary(x => x.GetProperty("studentId").GetGuid(), x => x.GetProperty("outcome").GetString());
        Assert.Equal("Submitted", recipients[users["student"]]);
        Assert.Equal("Missed", recipients[users["other-student"]]);
        Assert.Equal("Exempted", recipients[users["revoked-student"]]);
        Assert.Equal("NotObligated", recipients[users["late-student"]]);

        using var scope = factory.Services.CreateScope();
        var enrollment = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        var registration = await enrollment.Enrollments.SingleAsync(x => x.StudentId == users["other-student"] && x.ClassId == classId);
        Assert.Equal(1, registration.SkippedAssignmentCount);
        Assert.Equal(0, (await enrollment.Enrollments.SingleAsync(x => x.StudentId == users["student"])).SkippedAssignmentCount);
        var metrics = await scope.ServiceProvider.GetRequiredService<ICourseworkMetrics>().ForStudentAsync(users["other-student"],
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
        Assert.Equal((1, 0, 0, 1, (decimal?)null), (metrics.Assigned, metrics.Submitted, metrics.Graded, metrics.Missed, metrics.AverageScore));

        await As("other-student");
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine?submissionStatus=Missed");
        Assert.Equal(assignment, Assert.Single(mine.GetProperty("items").EnumerateArray()).GetProperty("assignment").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ResolveMissed_IsOffByDefault_AndLegacyAssignmentsAreNeverInferred()
    {
        await Enroll();
        var assignment = (await CreateAssignment("Legacy")).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            // Mô phỏng bài có trước migration: legacy, chưa công bố, không có snapshot.
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var item = await db.Assignments.SingleAsync(x => x.Id == assignment);
            db.Entry(item).Property(x => x.IsLegacy).CurrentValue = true;
            db.Entry(item).Property(x => x.PublishedAt).CurrentValue = null;
            db.AssignmentRecipients.RemoveRange(db.AssignmentRecipients.Where(x => x.AssignmentId == assignment));
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, (await Reconcile(TimeSpan.FromHours(3), resolve: false)).AssignmentsResolved);
        Assert.Equal(0, (await Reconcile(TimeSpan.FromHours(3), resolve: true)).AssignmentsResolved);
        Assert.Empty(factory.Missed);
        await As("student");
        var legacy = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/{assignment}");
        Assert.True(legacy.GetProperty("isLegacy").GetBoolean());
        Assert.Equal("Open", legacy.GetProperty("status").GetString());
        await Submit(assignment);
    }

    [Fact]
    public async Task OutboxRetryAndStaffReplay_RepublishWithTheSameBusinessKeys()
    {
        await Enroll(); await Enroll("other-student");
        var assignment = (await CreateAssignment("Replay")).GetProperty("id").GetGuid();
        var submission = await Submit(assignment);
        await As("teacher");
        Assert.Equal(HttpStatusCode.OK, (await Grade(submission, 5m, 0)).StatusCode);
        // Mô phỏng crash sau commit nhưng trước khi đánh dấu đã phát.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var item = await db.Assignments.SingleAsync(x => x.Id == assignment);
            db.Entry(item).Property(x => x.PublishedEventSentAt).CurrentValue = null;
            var grade = await db.SubmissionGrades.SingleAsync(x => x.SubmissionId == submission);
            db.Entry(grade).Property(x => x.EventSentAt).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        // Trong thời gian chờ, worker không phát đôi với request vừa commit.
        var early = await Reconcile(TimeSpan.Zero, resolve: false);
        Assert.Equal((0, 0), (early.PublishedRetried, early.GradesRetried));
        var retried = await Reconcile(TimeSpan.FromMinutes(5), resolve: false);
        Assert.Equal((1, 1), (retried.PublishedRetried, retried.GradesRetried));
        Assert.Equal(0, (await Reconcile(TimeSpan.FromMinutes(6), resolve: false)).PublishedRetried);

        await Reconcile(TimeSpan.FromHours(3), resolve: true);
        await As("teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/academic/assignments/{assignment}/events/replay", null)).StatusCode);
        await As("staff");
        var replay = await client.PostAsync($"/api/academic/assignments/{assignment}/events/replay", null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var counts = await Json(replay);
        Assert.Equal((1, 1, 1), (counts.GetProperty("published").GetInt32(), counts.GetProperty("graded").GetInt32(), counts.GetProperty("missed").GetInt32()));

        Assert.Equal(3, factory.Published.Count(x => x.AssignmentId == assignment));
        Assert.Single(factory.Published.Where(x => x.AssignmentId == assignment).Select(x => x.EventId).Distinct());
        Assert.Single(factory.Graded.Where(x => x.SubmissionId == submission).Select(x => (x.EventId, x.GradeVersion)).Distinct());
        Assert.Single(factory.Missed.Select(x => x.EventId).Distinct());
        Assert.Equal(2, factory.Missed.Count);
        // TV3 dedupe theo khóa nghiệp vụ: replay không cộng thêm vi phạm.
        using var check = factory.Services.CreateScope();
        Assert.Equal(1, (await check.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Enrollments
            .SingleAsync(x => x.StudentId == users["other-student"])).SkippedAssignmentCount);
    }

    [Fact]
    public async Task Practice_TeacherAndStaffManage_StudentsWithClassAccessRead()
    {
        var membership = await Enroll();
        await As("teacher");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/academic/practice", new { classId, title = " ", content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/academic/practice", new { classId, title = "x", content = new string('c', 20001) })).StatusCode);
        var created = await client.PostAsJsonAsync("/api/academic/practice", new { classId, title = "Đọc hiểu Unit 3", content = "<b>Đọc</b> đoạn văn và trả lời." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var practice = await Json(created);
        var id = practice.GetProperty("id").GetGuid();
        Assert.Equal(("GV teacher", "<b>Đọc</b> đoạn văn và trả lời.", 1), (practice.GetProperty("createdByName").GetString(),
            practice.GetProperty("content").GetString(), practice.GetProperty("version").GetInt32()));
        await client.PostAsJsonAsync("/api/academic/practice", new { classId, title = "Ngữ pháp", content = "Thì hiện tại" });

        await As("other-teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/academic/practice", new { classId, title = "x", content = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/practice/{id}")).StatusCode);

        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/academic/practice", new { classId, title = "x", content = "y" })).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/academic/practice?classId={classId}&search=unit");
        var summary = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal((id, "GV teacher"), (summary.GetProperty("id").GetGuid(), summary.GetProperty("createdByName").GetString()));
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>($"/api/academic/practice?classId={classId}")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/academic/practice/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/academic/practice/{id}", new { title = "x", content = "y", expectedVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/practice?classId={otherClassId}")).StatusCode);
        await As("other-student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/practice/{id}")).StatusCode);

        await As("staff");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/academic/practice/{id}", new { title = "Đọc hiểu", content = "Mới", expectedVersion = 7 })).StatusCode);
        var updated = await client.PutAsJsonAsync($"/api/academic/practice/{id}", new { title = "Đọc hiểu (sửa)", content = "Mới", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await Json(updated)).GetProperty("version").GetInt32());
        // Retry cùng nội dung sau khi mất response.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/academic/practice/{id}", new { title = "Đọc hiểu (sửa)", content = "Mới", expectedVersion = 1 })).StatusCode);

        await Revoke(membership);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/practice/{id}")).StatusCode);

        await As("teacher");
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/academic/practice/{id}?expectedVersion=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/academic/practice/{id}?expectedVersion=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/academic/practice/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/practice/{id}")).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/api/academic/practice?classId={classId}")).GetProperty("total").GetInt32());
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().PracticeExercises.SingleAsync(x => x.Id == id);
        Assert.Equal(users["teacher"], stored.DeletedBy);
    }

    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + shift;
    }

    private sealed class CourseworkFactory : WebApplicationFactory<Program>,
        IIntegrationEventHandler<AssignmentPublishedIntegrationEvent>, IIntegrationEventHandler<SubmissionGradedIntegrationEvent>,
        IIntegrationEventHandler<AssignmentMissedIntegrationEvent>
    {
        private readonly List<SqliteConnection> connections = [];
        public ConcurrentQueue<AssignmentPublishedIntegrationEvent> Published { get; } = new();
        public ConcurrentQueue<SubmissionGradedIntegrationEvent> Graded { get; } = new();
        public ConcurrentQueue<AssignmentMissedIntegrationEvent> Missed { get; } = new();
        public Task HandleAsync(AssignmentPublishedIntegrationEvent value, CancellationToken ct = default) { Published.Enqueue(value); return Task.CompletedTask; }
        public Task HandleAsync(SubmissionGradedIntegrationEvent value, CancellationToken ct = default) { Graded.Enqueue(value); return Task.CompletedTask; }
        public Task HandleAsync(AssignmentMissedIntegrationEvent value, CancellationToken ct = default) { Missed.Enqueue(value); return Task.CompletedTask; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Identity:InitializeDatabase"] = "false", ["Academic:InitializeDatabase"] = "false",
                ["Enrollment:InitializeDatabase"] = "false", ["MediaNotification:InitializeDatabase"] = "false",
                ["Finance:InitializeDatabase"] = "false", ["Academic:Coursework:WorkerEnabled"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                Sqlite<IdentityDbContext>(services); Sqlite<AcademicDbContext>(services);
                Sqlite<EnrollmentDbContext>(services); Sqlite<MediaNotificationDbContext>(services);
                Sqlite<FinanceDbContext>(services);
                services.AddSingleton<IIntegrationEventHandler<AssignmentPublishedIntegrationEvent>>(this);
                services.AddSingleton<IIntegrationEventHandler<SubmissionGradedIntegrationEvent>>(this);
                services.AddSingleton<IIntegrationEventHandler<AssignmentMissedIntegrationEvent>>(this);
                services.RemoveAll<IObjectStorage>(); services.AddSingleton<IObjectStorage, NullStorage>();
            });
        }
        private void Sqlite<T>(IServiceCollection services) where T : DbContext
        {
            var connection = new SqliteConnection("Data Source=:memory:"); connection.Open(); connections.Add(connection);
            services.RemoveAll<DbContextOptions<T>>(); services.RemoveAll<IDbContextOptionsConfiguration<T>>();
            services.AddDbContext<T>(o => o.UseSqlite(connection));
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync(); foreach (var connection in connections) await connection.DisposeAsync();
        }
    }

    private sealed class NullStorage : IObjectStorage
    {
        public Task<Uri> PresignGetAsync(string key, string name, DateTimeOffset expiresAt, CancellationToken ct) => Task.FromResult(new Uri("https://storage.example/" + key));
        public Task<Uri> PresignPutAsync(string key, string type, DateTimeOffset expiresAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task CopyAsync(string source, string destination, string etag, string type, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
