using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using EnglishCenter.Modules.MediaNotification.Domain.Feed;
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

// TV4: inbox thông báo, phản hồi, FAQ và bảng tin lớp. Chạy trên host thật với SQLite; quyền lớp lấy từ Enrollment/Academic thật.
public sealed class Tv4NotificationFeedbackTests : IAsyncLifetime
{
    private const string Password = "Tv4-test-password-123!";
    private const string Api = "/api/media-notification";
    private readonly Tv4Factory factory = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, Guid> users = [];
    private readonly Dictionary<string, string> tokens = [];
    private Guid classId, otherClassId;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var identity = sp.GetRequiredService<IdentityDbContext>();
        var academic = sp.GetRequiredService<AcademicDbContext>();
        await identity.Database.EnsureCreatedAsync();
        await academic.Database.EnsureCreatedAsync();
        await sp.GetRequiredService<EnrollmentDbContext>().Database.EnsureCreatedAsync();
        await sp.GetRequiredService<FinanceDbContext>().Database.EnsureCreatedAsync();
        await sp.GetRequiredService<MediaNotificationDbContext>().Database.EnsureCreatedAsync();
        var passwords = sp.GetRequiredService<IPasswordService>();
        foreach (var (name, role) in new[] { ("admin", Roles.Admin), ("staff", Roles.Staff), ("teacher", Roles.Teacher),
                     ("other-teacher", Roles.Teacher), ("student", Roles.Student), ("classmate", Roles.Student),
                     ("outsider", Roles.Student) })
        {
            var user = User.Create($"{name}@example.test", name, role, passwords.Hash(Password), DateTimeOffset.UtcNow);
            identity.Users.Add(user);
            users[name] = user.Id;
            if (role == Roles.Teacher) academic.Teachers.Add(Teacher.Create(user.Id, name, user.Email));
        }
        await identity.SaveChangesAsync();
        var course = Course.Create(Guid.NewGuid(), "TV4", "TV4 course");
        academic.Courses.Add(course);
        var main = Class.Create("TV4-01", "TV4 main", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 12, 31), 20, 0,
            users["teacher"], DateTimeOffset.UtcNow);
        var other = Class.Create("TV4-02", "TV4 other", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 12, 31), 20, 0,
            users["other-teacher"], DateTimeOffset.UtcNow);
        academic.Classes.AddRange(main, other);
        classId = main.Id;
        otherClassId = other.Id;
        await academic.SaveChangesAsync();
        var enrollment = sp.GetRequiredService<EnrollmentDbContext>();
        foreach (var name in new[] { "student", "classmate", "outsider" })
            enrollment.Students.Add(new Student(users[name], name, $"{name}@example.test"));
        enrollment.Classes.Add(new ClassSnapshot(main.Id, main.Code, main.StartDate, main.EndDate, main.MaxStudents, main.TuitionFee));
        await enrollment.SaveChangesAsync();
    }

    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }

    // ---------- helpers ----------

    // Đăng nhập bị giới hạn 20 lần/phút/host nên mỗi người dùng chỉ đăng nhập một lần.
    private async Task As(string name)
    {
        if (!tokens.TryGetValue(name, out var accessToken))
        {
            client.DefaultRequestHeaders.Authorization = null;
            var response = await client.PostAsJsonAsync("/api/identity/auth/login", new { email = $"{name}@example.test", password = Password });
            response.EnsureSuccessStatusCode();
            tokens[name] = accessToken = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task<Guid> RegistrationOf(string name)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Enrollments
            .SingleAsync(x => x.StudentId == users[name])).Id;
    }

    private void Anonymous() => client.DefaultRequestHeaders.Authorization = null;

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null) =>
        client.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) });

    private async Task<HttpResponseMessage> Expect(HttpStatusCode status, HttpMethod method, string url, object? body = null)
    {
        var response = await Send(method, url, body);
        Assert.True(response.StatusCode == status, $"{method} {url}: expected {status}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private async Task<JsonElement> Get(string url) => await Json(await Expect(HttpStatusCode.OK, HttpMethod.Get, url));

    private async Task Enroll(string name)
    {
        await As("admin");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        if (!await db.Enrollments.AnyAsync(x => x.ClassId == classId && x.StudentId == users[name]))
            await Expect(HttpStatusCode.Created, HttpMethod.Post, "/api/enrollment/enrollments", new
            { classId, studentId = users[name], maxAbsences = 3, maxLates = 5, maxSkippedAssignments = 2 });
        await Expect(HttpStatusCode.Created, HttpMethod.Post, "/api/enrollment/memberships", new { classId, studentId = users[name] });
    }

    private async Task Publish<T>(T integrationEvent) where T : IIntegrationEvent
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(integrationEvent, default, throwOnHandlerFailure: true);
    }

    private async Task<R> Db<R>(Func<MediaNotificationDbContext, Task<R>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>());
    }

    private async Task<JsonElement> Inbox(string url = "") => await Get($"{Api}/notifications{url}");

    // ---------- US-25-27: inbox ----------

    [Fact]
    public async Task Inbox_RequiresAuthentication()
    {
        Anonymous();
        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Get, $"{Api}/notifications"), (HttpMethod.Get, $"{Api}/notifications/unread-count"),
                     (HttpMethod.Post, $"{Api}/notifications/{Guid.NewGuid()}/read"), (HttpMethod.Post, $"{Api}/notifications/read-all")
                 })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url)).StatusCode);
    }

    [Theory]
    [InlineData("student")]
    [InlineData("teacher")]
    [InlineData("staff")]
    [InlineData("admin")]
    public async Task Inbox_EveryRoleReadsItsOwnEmptyInbox(string name)
    {
        await As(name);
        var inbox = await Inbox();
        Assert.Equal(0, inbox.GetProperty("total").GetInt32());
        Assert.Equal(0, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Equal(20, inbox.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Inbox_LockedAccount_IsRejectedEvenWithAnExistingToken()
    {
        await As("student");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.SingleAsync(x => x.Id == users["student"])).SetLocked(true);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Api}/notifications/read-all")).StatusCode);
    }

    [Fact]
    public async Task Enrollment_NotifiesOnlyTheStudent_ReplayDoesNotDuplicate_ReadStateAndBadge()
    {
        await Enroll("student");
        var registration = await RegistrationOf("student");

        await As("student");
        var inbox = await Inbox();
        var item = Assert.Single(Items(inbox));
        Assert.Equal("StudentEnrolled", item.GetProperty("kind").GetString());
        Assert.Equal("Class", item.GetProperty("targetType").GetString());
        Assert.Equal(classId, item.GetProperty("targetId").GetGuid());
        Assert.False(item.GetProperty("isRead").GetBoolean());
        Assert.Equal(1, inbox.GetProperty("unreadCount").GetInt32());

        await As("admin");
        foreach (var _ in new[] { 1, 2 })
            await Expect(HttpStatusCode.Accepted, HttpMethod.Post, $"/api/enrollment/enrollments/{registration}/replay");

        await As("student");
        Assert.Equal(1, (await Inbox()).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Get($"{Api}/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
        var id = Items(await Inbox())[0].GetProperty("id").GetGuid();

        // Người khác không đọc, không đánh dấu được thông báo của student (404, không lộ sự tồn tại).
        await As("classmate");
        Assert.Equal(0, (await Inbox()).GetProperty("total").GetInt32());
        await Expect(HttpStatusCode.NotFound, HttpMethod.Post, $"{Api}/notifications/{id}/read");

        await As("student");
        Assert.Equal(1, (await Get($"{Api}/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
        await Expect(HttpStatusCode.NoContent, HttpMethod.Post, $"{Api}/notifications/{id}/read");
        var readAt = Items(await Inbox())[0].GetProperty("readAtUtc").GetDateTime();
        await Expect(HttpStatusCode.NoContent, HttpMethod.Post, $"{Api}/notifications/{id}/read");
        Assert.Equal(readAt, Items(await Inbox())[0].GetProperty("readAtUtc").GetDateTime());
        Assert.Equal(0, (await Get($"{Api}/notifications/unread-count")).GetProperty("unreadCount").GetInt32());
        await Expect(HttpStatusCode.NotFound, HttpMethod.Post, $"{Api}/notifications/{Guid.NewGuid()}/read");
    }

    [Fact]
    public async Task Events_CreateGenericNotifications_DedupedByBusinessKey_AndCarryNoScoreOrUrl()
    {
        var student = users["student"];
        var (submission, assignment, invoice, warning) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var graded = new SubmissionGradedIntegrationEvent(submission, assignment, student, 9.5m) { GradeVersion = 1, ClassId = classId };
        await Publish(graded);
        await Publish(graded with { EventId = Guid.NewGuid() });
        await Publish(graded with { GradeVersion = 2, Score = 7.25m });
        await Publish(new InvoicePaidIntegrationEvent(invoice, Guid.NewGuid(), student, DateTime.UtcNow));
        await Publish(new InvoicePaidIntegrationEvent(invoice, Guid.NewGuid(), student, DateTime.UtcNow));
        var issued = new WarningIssuedIntegrationEvent(warning, Guid.NewGuid(), student, classId, "Absence");
        await Publish(issued);
        await Publish(issued);

        await As("student");
        var items = Items(await Inbox());
        Assert.Equal(new[] { "InvoicePaid", "SubmissionGraded", "SubmissionGraded", "WarningIssued" },
            items.Select(x => x.GetProperty("kind").GetString()!).Order().ToArray());
        Assert.Equal(assignment, items.First(x => x.GetProperty("kind").GetString() == "SubmissionGraded").GetProperty("targetId").GetGuid());
        var raw = JsonSerializer.Serialize(items);
        foreach (var forbidden in new[] { "9.5", "7.25", "score", "http", "signature", "fileKey" })
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);

        await As("classmate");
        Assert.Equal(0, (await Inbox()).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AssignmentPublished_NotifiesSnapshotRecipientsOnly_AndReplayIsIdempotent()
    {
        await Enroll("student");
        await As("teacher");
        var now = DateTimeOffset.UtcNow;
        var created = await Expect(HttpStatusCode.Created, HttpMethod.Post, "/api/academic/assignments", new
        { classId, title = "TV4 homework", description = "x", opensAtUtc = now.AddHours(-1), closesAtUtc = now.AddHours(2) });
        var assignment = (await Json(created)).GetProperty("id").GetGuid();

        async Task<List<JsonElement>> Published(string name)
        {
            await As(name);
            return Items(await Inbox()).Where(x => x.GetProperty("kind").GetString() == "AssignmentPublished").ToList();
        }

        var mine = Assert.Single(await Published("student"));
        Assert.Equal("Assignment", mine.GetProperty("targetType").GetString());
        Assert.Equal(assignment, mine.GetProperty("targetId").GetGuid());
        foreach (var name in new[] { "classmate", "outsider", "teacher", "other-teacher", "staff" })
            Assert.Empty(await Published(name));

        await As("staff");
        await Expect(HttpStatusCode.OK, HttpMethod.Post, $"/api/academic/assignments/{assignment}/events/replay");
        await Expect(HttpStatusCode.OK, HttpMethod.Post, $"/api/academic/assignments/{assignment}/events/replay");
        Assert.Single(await Published("student"));
        Assert.Equal(1, await Db(db => db.Notifications.CountAsync(x => x.Kind == "AssignmentPublished")));
    }

    [Fact]
    public async Task Inbox_PaginatesWithDefaultAndMaximumSize_AndFiltersUnread()
    {
        var student = users["student"];
        for (var i = 0; i < 25; i++)
            await Publish(new InvoicePaidIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), student, DateTime.UtcNow));
        await As("student");
        var first = await Inbox();
        Assert.Equal(25, first.GetProperty("total").GetInt32());
        Assert.Equal(20, Items(first).Count);
        Assert.Equal(5, Items(await Inbox("?page=2")).Count);
        Assert.Equal(100, (await Inbox("?pageSize=1000")).GetProperty("pageSize").GetInt32());
        foreach (var bad in new[] { "?page=0", "?pageSize=0", "?page=-1", "?page=1000001" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, $"{Api}/notifications{bad}")).StatusCode);

        var firstId = Items(first)[0].GetProperty("id").GetGuid();
        await Expect(HttpStatusCode.NoContent, HttpMethod.Post, $"{Api}/notifications/{firstId}/read");
        var unread = await Inbox("?unreadOnly=true&pageSize=100");
        Assert.Equal(24, unread.GetProperty("total").GetInt32());
        Assert.DoesNotContain(Items(unread), x => x.GetProperty("id").GetGuid() == firstId);
        var all = await Expect(HttpStatusCode.OK, HttpMethod.Post, $"{Api}/notifications/read-all");
        Assert.Equal(24, (await Json(all)).GetProperty("marked").GetInt32());
        Assert.Equal(0, (await Inbox()).GetProperty("unreadCount").GetInt32());
        Assert.Equal(0, (await Json(await Expect(HttpStatusCode.OK, HttpMethod.Post, $"{Api}/notifications/read-all"))).GetProperty("marked").GetInt32());
    }

    [Fact]
    public async Task ReadAll_OnlyTouchesTheCallersNotifications()
    {
        await Publish(new InvoicePaidIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), users["student"], DateTime.UtcNow));
        await Publish(new InvoicePaidIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), users["classmate"], DateTime.UtcNow));
        await As("student");
        Assert.Equal(1, (await Json(await Expect(HttpStatusCode.OK, HttpMethod.Post, $"{Api}/notifications/read-all"))).GetProperty("marked").GetInt32());
        await As("classmate");
        Assert.Equal(1, (await Inbox()).GetProperty("unreadCount").GetInt32());
    }

    // ---------- US-28: phản hồi ----------

    private static object Feedback(string title = "Phòng học nóng", string content = "Điều hòa hỏng", int? rating = 3) =>
        new { title, content, rating };

    private async Task<JsonElement> CreateFeedback(string student = "student", object? body = null)
    {
        await As(student);
        return await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/feedback", body ?? Feedback()));
    }

    [Fact]
    public async Task Feedback_RequiresAuthenticationAndRole()
    {
        Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Post, $"{Api}/feedback", Feedback())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, $"{Api}/feedback")).StatusCode);
        var created = await CreateFeedback();
        var id = created.GetProperty("id").GetGuid();
        // Chỉ Student gửi; Teacher không dùng chức năng phản hồi.
        foreach (var name in new[] { "teacher", "staff", "admin" })
        {
            await As(name);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Api}/feedback", Feedback())).StatusCode);
        }
        await As("teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/feedback")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/feedback/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Patch, $"{Api}/feedback/{id}/status",
            new { status = "InProgress", expectedRevision = created.GetProperty("revision").GetGuid() })).StatusCode);
    }

    [Fact]
    public async Task Feedback_Create_TrimsStoresUtcAndValidates()
    {
        var created = await CreateFeedback(body: Feedback("  Tiêu đề  ", "  Nội dung\nnhiều dòng  ", 5));
        Assert.Equal("Tiêu đề", created.GetProperty("title").GetString());
        Assert.Equal("Nội dung\nnhiều dòng", created.GetProperty("content").GetString());
        Assert.Equal("New", created.GetProperty("status").GetString());
        Assert.Equal(users["student"], created.GetProperty("studentId").GetGuid());
        Assert.True(DateTime.UtcNow - created.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime() < TimeSpan.FromMinutes(5));

        await As("student");
        var invalid = new object[]
        {
            Feedback(title: ""), Feedback(title: "   "), Feedback(title: new string('a', 151)), Feedback(title: "bad\u0007title"),
            Feedback(title: "line\nbreak"), Feedback(content: ""), Feedback(content: new string('a', 2001)),
            Feedback(content: "bell\u0007"), Feedback(rating: 0), Feedback(rating: 6), Feedback(rating: null),
            new { title = "x", content = "y" }
        };
        foreach (var body in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Api}/feedback", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, $"{Api}/feedback", Feedback(title: new string('a', 150), rating: 1))).StatusCode);
        Assert.Equal(2, await Db(db => db.Feedbacks.CountAsync()));
    }

    [Fact]
    public async Task Feedback_StudentSeesOnlyOwn_StaffAndAdminSeeAll_WithSearchStatusAndPaging()
    {
        var mine = await CreateFeedback("student", Feedback("Wifi yếu", "Mạng chậm", 2));
        await CreateFeedback("classmate", Feedback("Giáo viên tốt", "Rất nhiệt tình", 5));
        await CreateFeedback("classmate", Feedback("Khác", "Wifi cũng yếu", 1));
        var id = mine.GetProperty("id").GetGuid();

        await As("student");
        Assert.Equal(1, (await Get($"{Api}/feedback")).GetProperty("total").GetInt32());
        await Expect(HttpStatusCode.OK, HttpMethod.Get, $"{Api}/feedback/{id}");
        await As("classmate");
        await Expect(HttpStatusCode.NotFound, HttpMethod.Get, $"{Api}/feedback/{id}");
        Assert.Equal(2, (await Get($"{Api}/feedback")).GetProperty("total").GetInt32());

        foreach (var name in new[] { "staff", "admin" })
        {
            await As(name);
            Assert.Equal(3, (await Get($"{Api}/feedback")).GetProperty("total").GetInt32());
            Assert.Equal(2, (await Get($"{Api}/feedback?search=wifi")).GetProperty("total").GetInt32());
            Assert.Equal(3, (await Get($"{Api}/feedback?status=New")).GetProperty("total").GetInt32());
            Assert.Equal(0, (await Get($"{Api}/feedback?status=Resolved")).GetProperty("total").GetInt32());
            var page = await Get($"{Api}/feedback?page=2&pageSize=2");
            Assert.Single(Items(page));
            Assert.Equal(3, page.GetProperty("total").GetInt32());
            Assert.Equal(100, (await Get($"{Api}/feedback?pageSize=500")).GetProperty("pageSize").GetInt32());
            foreach (var bad in new[] { "?status=Foo", "?page=0", "?pageSize=0", $"?search={new string('a', 101)}" })
                Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, $"{Api}/feedback{bad}")).StatusCode);
        }

        // Student thử lọc theo chủ đề của người khác: vẫn chỉ thấy phản hồi của mình.
        await As("student");
        Assert.Equal(1, (await Get($"{Api}/feedback?search=wifi")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Feedback_StatusFlow_ValidTransitions_RevisionConflict_AndNoReopen()
    {
        var created = await CreateFeedback();
        var id = created.GetProperty("id").GetGuid();
        var r0 = created.GetProperty("revision").GetGuid();
        async Task<HttpResponseMessage> Patch(Guid revision, string status) => await Send(HttpMethod.Patch, $"{Api}/feedback/{id}/status",
            new { status, expectedRevision = revision });

        // Student không đổi được trạng thái.
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(r0, "InProgress")).StatusCode);

        await As("staff");
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(r0, "Foo")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Patch, $"{Api}/feedback/{id}/status", new { status = "InProgress" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Patch, $"{Api}/feedback/{Guid.NewGuid()}/status",
            new { status = "InProgress", expectedRevision = r0 })).StatusCode);
        // Không nhảy cóc New → Resolved.
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(r0, "Resolved")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(r0, "New")).StatusCode);

        var progress = await Json(Expect200(await Patch(r0, "InProgress")));
        Assert.Equal("InProgress", progress.GetProperty("status").GetString());
        Assert.Equal(users["staff"], progress.GetProperty("handledBy").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, progress.GetProperty("handledAtUtc").ValueKind);
        var r1 = progress.GetProperty("revision").GetGuid();
        Assert.NotEqual(r0, r1);

        // Revision cũ → 409 và không đổi dữ liệu.
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(r0, "Resolved")).StatusCode);
        await As("admin");
        var resolved = await Json(Expect200(await Patch(r1, "Resolved")));
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(users["admin"], resolved.GetProperty("handledBy").GetGuid());
        var r2 = resolved.GetProperty("revision").GetGuid();

        // Không mở lại.
        foreach (var status in new[] { "New", "InProgress", "Resolved" })
            Assert.Equal(HttpStatusCode.Conflict, (await Patch(r2, status)).StatusCode);

        // Student thấy trạng thái mới nhưng không thấy người xử lý.
        await As("student");
        var seen = await Get($"{Api}/feedback/{id}");
        Assert.Equal("Resolved", seen.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("handledBy").ValueKind);
    }

    [Fact]
    public async Task Feedback_ConcurrentStatusChange_OnlyOneWins()
    {
        var created = await CreateFeedback();
        var id = created.GetProperty("id").GetGuid();
        var revision = created.GetProperty("revision").GetGuid();
        await As("staff");
        var first = await Send(HttpMethod.Patch, $"{Api}/feedback/{id}/status", new { status = "InProgress", expectedRevision = revision });
        var second = await Send(HttpMethod.Patch, $"{Api}/feedback/{id}/status", new { status = "InProgress", expectedRevision = revision });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
    }

    private static HttpResponseMessage Expect200(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    // ---------- US-30: FAQ ----------

    private object Faq(string question = "How to pay tuition?", string answer = "Pay at the front desk.", bool published = true) =>
        new { question, answer, isPublished = published };

    [Fact]
    public async Task Faq_AnonymousReadsOnlyPublished_DraftsNeverLeak()
    {
        await As("admin");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/admin/faqs", Faq("Tuition payment", "Pay online"));
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/admin/faqs", Faq("Secret draft policy", "Not yet public", published: false));

        Anonymous();
        var list = await Get($"{Api}/faqs");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Tuition payment", Items(list)[0].GetProperty("question").GetString());
        Assert.Equal(0, (await Get($"{Api}/faqs?search=secret")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Get($"{Api}/faqs?search=not yet public")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Get($"{Api}/faqs?search=TUITION")).GetProperty("total").GetInt32());
        Assert.DoesNotContain("Secret", JsonSerializer.Serialize(list));
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, $"{Api}/faqs?page=0")).StatusCode);
        Assert.Equal(100, (await Get($"{Api}/faqs?pageSize=1000")).GetProperty("pageSize").GetInt32());

        // Người đã đăng nhập vẫn chỉ thấy bản đã xuất bản ở API công khai.
        await As("student");
        Assert.Equal(1, (await Get($"{Api}/faqs")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Faq_AdminApi_IsAdminOnly()
    {
        await As("admin");
        var id = (await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/admin/faqs", Faq()))).GetProperty("id").GetGuid();

        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, $"{Api}/admin/faqs", null), (HttpMethod.Post, $"{Api}/admin/faqs", Faq()),
            (HttpMethod.Put, $"{Api}/admin/faqs/{id}", Faq()), (HttpMethod.Delete, $"{Api}/admin/faqs/{id}", null)
        };
        Anonymous();
        foreach (var call in calls)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(call.Method, call.Url, call.Body)).StatusCode);
        foreach (var name in new[] { "student", "teacher", "staff" })
        {
            await As(name);
            foreach (var call in calls)
                Assert.Equal(HttpStatusCode.Forbidden, (await Send(call.Method, call.Url, call.Body)).StatusCode);
        }
        await As("admin");
        Assert.Equal(1, (await Get($"{Api}/admin/faqs")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Faq_AdminCreateUpdateHideShowDelete_AndValidation()
    {
        await As("admin");
        var created = await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/admin/faqs", Faq("  Q1  ", "  A1  ", published: false)));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Q1", created.GetProperty("question").GetString());
        Assert.False(created.GetProperty("isPublished").GetBoolean());
        Assert.Equal(1, (await Get($"{Api}/admin/faqs?published=false")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Get($"{Api}/admin/faqs?published=true")).GetProperty("total").GetInt32());

        Anonymous();
        Assert.Equal(0, (await Get($"{Api}/faqs")).GetProperty("total").GetInt32());
        await As("admin");
        await Expect(HttpStatusCode.OK, HttpMethod.Put, $"{Api}/admin/faqs/{id}", Faq("Q2", "A2", published: true));
        Anonymous();
        var shown = Assert.Single(Items(await Get($"{Api}/faqs")));
        Assert.Equal("Q2", shown.GetProperty("question").GetString());
        await As("admin");
        await Expect(HttpStatusCode.OK, HttpMethod.Put, $"{Api}/admin/faqs/{id}", Faq("Q2", "A2", published: false));
        Anonymous();
        Assert.Equal(0, (await Get($"{Api}/faqs")).GetProperty("total").GetInt32());

        await As("admin");
        foreach (var bad in new[] { Faq(question: ""), Faq(question: new string('q', 301)), Faq(answer: " "), Faq(answer: new string('a', 4001)),
                     Faq(question: "bell\u0007") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Api}/admin/faqs", bad)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, $"{Api}/admin/faqs/{id}", bad)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"{Api}/admin/faqs/{Guid.NewGuid()}", Faq())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"{Api}/admin/faqs/{Guid.NewGuid()}")).StatusCode);

        await Expect(HttpStatusCode.NoContent, HttpMethod.Delete, $"{Api}/admin/faqs/{id}");
        await Expect(HttpStatusCode.NotFound, HttpMethod.Delete, $"{Api}/admin/faqs/{id}");
        Assert.Equal(0, (await Get($"{Api}/admin/faqs")).GetProperty("total").GetInt32());
    }

    // ---------- US-31: bảng tin theo lớp ----------

    private async Task<Guid> Post(string name, string content = "Xin chào cả lớp", Guid? inClass = null)
    {
        await As(name);
        return (await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/classes/{inClass ?? classId}/posts", new { content })))
            .GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> Posts(string name, Guid? inClass = null, string query = "")
    {
        await As(name);
        return await Get($"{Api}/classes/{inClass ?? classId}/posts{query}");
    }

    private async Task EnrollMembers()
    {
        await Enroll("student");
        await Enroll("classmate");
    }

    [Fact]
    public async Task Feed_RequiresAuthentication()
    {
        var id = Guid.NewGuid();
        Anonymous();
        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Get, $"{Api}/classes/{classId}/posts"), (HttpMethod.Post, $"{Api}/classes/{classId}/posts"),
                     (HttpMethod.Put, $"{Api}/posts/{id}"), (HttpMethod.Delete, $"{Api}/posts/{id}"), (HttpMethod.Put, $"{Api}/posts/{id}/like"),
                     (HttpMethod.Get, $"{Api}/posts/{id}/comments"), (HttpMethod.Put, $"{Api}/comments/{id}"), (HttpMethod.Delete, $"{Api}/comments/{id}")
                 })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(method, url, method == HttpMethod.Get ? null : new { content = "x", hidden = true })).StatusCode);
    }

    [Fact]
    public async Task Feed_ClassAccess_StudentMembersTeacherOfClassStaffAdmin_Only()
    {
        await EnrollMembers();
        var post = await Post("student");

        foreach (var name in new[] { "student", "classmate", "teacher", "staff", "admin" })
        {
            var list = await Posts(name);
            Assert.Equal(post, Assert.Single(Items(list)).GetProperty("id").GetGuid());
        }
        foreach (var name in new[] { "student", "classmate", "teacher", "staff", "admin" })
            await Post(name, $"Bài của {name}");
        Assert.Equal(6, (await Posts("admin")).GetProperty("total").GetInt32());

        // Đoán ID lớp: học viên chưa ghi danh, giáo viên lớp khác bị chặn cả đọc lẫn đăng.
        foreach (var name in new[] { "outsider", "other-teacher" })
        {
            await As(name);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/classes/{classId}/posts")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = "spam" })).StatusCode);
        }
        // Học viên ghi danh lớp 1 không có quyền ở lớp 2.
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/classes/{otherClassId}/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Api}/classes/{otherClassId}/posts", new { content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, $"{Api}/classes/{Guid.NewGuid()}/posts")).StatusCode);
        // Staff/Admin vào được mọi lớp; giáo viên lớp 2 vào lớp 2.
        await Post("staff", "Thông báo lớp 2", otherClassId);
        Assert.Single(Items(await Posts("other-teacher", otherClassId)));
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/classes/{classId}/posts")).StatusCode);
        await As("teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/classes/{otherClassId}/posts")).StatusCode);
    }

    [Fact]
    public async Task Feed_PostAndCommentIdsOfAnotherClass_AreNotFoundForOutsiders()
    {
        await EnrollMembers();
        var post = await Post("student");
        var comment = (await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "hi" })))
            .GetProperty("id").GetGuid();

        foreach (var name in new[] { "outsider", "other-teacher" })
        {
            await As(name);
            var calls = new (HttpMethod, string, object?)[]
            {
                (HttpMethod.Get, $"{Api}/posts/{post}/comments", null), (HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "x" }),
                (HttpMethod.Put, $"{Api}/posts/{post}", new { content = "x" }), (HttpMethod.Delete, $"{Api}/posts/{post}", null),
                (HttpMethod.Put, $"{Api}/posts/{post}/hidden", new { hidden = true }), (HttpMethod.Put, $"{Api}/posts/{post}/like", null),
                (HttpMethod.Delete, $"{Api}/posts/{post}/like", null), (HttpMethod.Put, $"{Api}/comments/{comment}", new { content = "x" }),
                (HttpMethod.Delete, $"{Api}/comments/{comment}", null), (HttpMethod.Put, $"{Api}/comments/{comment}/hidden", new { hidden = true })
            };
            foreach (var (method, url, body) in calls)
                Assert.Equal(HttpStatusCode.NotFound, (await Send(method, url, body)).StatusCode);
        }
        // Không có tác dụng phụ.
        Assert.Equal("Xin chào cả lớp", await Db(async db => (await db.Posts.SingleAsync()).Content));
        Assert.Equal(0, await Db(db => db.Likes.CountAsync()));
        Assert.Equal(FeedState.Visible, await Db(async db => (await db.Posts.SingleAsync()).State));
    }

    [Fact]
    public async Task Feed_OnlyAuthorEditsOwnContent_ModeratorsHideAndRemoveWithAudit()
    {
        await EnrollMembers();
        var post = await Post("student", "Bài gốc");

        // Người khác trong lớp (kể cả giáo viên/staff/admin) không sửa bài của người khác; classmate không xóa/ẩn.
        foreach (var name in new[] { "classmate", "teacher", "staff", "admin" })
        {
            await As(name);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"{Api}/posts/{post}", new { content = "đổi" })).StatusCode);
        }
        await As("classmate");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, $"{Api}/posts/{post}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"{Api}/posts/{post}/hidden", new { hidden = true })).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"{Api}/posts/{post}/hidden", new { hidden = true })).StatusCode);
        await Expect(HttpStatusCode.NoContent, HttpMethod.Put, $"{Api}/posts/{post}", new { content = "  Bài đã sửa  " });
        var edited = Assert.Single(Items(await Posts("student")));
        Assert.Equal("Bài đã sửa", edited.GetProperty("content").GetString());
        Assert.NotEqual(JsonValueKind.Null, edited.GetProperty("updatedAtUtc").ValueKind);

        // Giáo viên đúng lớp ẩn: học viên không thấy, giáo viên/staff thấy kèm cờ isHidden.
        await As("teacher");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Put, $"{Api}/posts/{post}/hidden", new { hidden = true });
        Assert.Empty(Items(await Posts("student")));
        Assert.Empty(Items(await Posts("classmate")));
        Assert.True(Assert.Single(Items(await Posts("staff"))).GetProperty("isHidden").GetBoolean());
        Assert.True(Assert.Single(Items(await Posts("teacher"))).GetProperty("isHidden").GetBoolean());
        await As("classmate");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"{Api}/posts/{post}/like")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "x" })).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"{Api}/posts/{post}", new { content = "x" })).StatusCode);
        Assert.Equal((users["teacher"], FeedState.Hidden), await Db(async db =>
        { var x = await db.Posts.SingleAsync(); return (x.ModeratedBy!.Value, x.State); }));

        await As("staff");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Put, $"{Api}/posts/{post}/hidden", new { hidden = false });
        Assert.Single(Items(await Posts("student")));
        await As("staff");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, $"{Api}/posts/{post}/hidden", new { })).StatusCode);

        // Admin xóa: biến mất với mọi người, lưu người thực hiện.
        await As("admin");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Delete, $"{Api}/posts/{post}");
        foreach (var name in new[] { "student", "teacher", "admin" })
            Assert.Empty(Items(await Posts(name)));
        await Expect(HttpStatusCode.NotFound, HttpMethod.Delete, $"{Api}/posts/{post}");
        Assert.Equal((users["admin"], FeedState.Removed), await Db(async db =>
        { var x = await db.Posts.SingleAsync(); return (x.ModeratedBy!.Value, x.State); }));
    }

    [Fact]
    public async Task Feed_AuthorDeletesOwnPost_AndOtherClassTeacherCannotModerate()
    {
        await EnrollMembers();
        var post = await Post("student");
        await As("other-teacher");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Delete, $"{Api}/posts/{post}")).StatusCode);
        await As("student");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Delete, $"{Api}/posts/{post}");
        Assert.Empty(Items(await Posts("student")));
        Assert.Equal((users["student"], FeedState.Removed), await Db(async db =>
        { var x = await db.Posts.SingleAsync(); return (x.ModeratedBy!.Value, x.State); }));
    }

    [Fact]
    public async Task Feed_Comments_CreateListEditRemoveHide_WithAuthorAndModeratorRules()
    {
        await EnrollMembers();
        var post = await Post("student");
        await As("classmate");
        var comment = await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "  Hay quá  " }));
        var id = comment.GetProperty("id").GetGuid();
        Assert.Equal("Hay quá", comment.GetProperty("content").GetString());
        Assert.Equal("Student", comment.GetProperty("authorRole").GetString());
        await As("teacher");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "Cảm ơn" });

        Assert.Equal(2, (await Get($"{Api}/posts/{post}/comments")).GetProperty("total").GetInt32());
        Assert.Equal(2, Assert.Single(Items(await Posts("student"))).GetProperty("commentCount").GetInt32());

        // Chỉ tác giả sửa; giáo viên không sửa hộ.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"{Api}/comments/{id}", new { content = "x" })).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"{Api}/comments/{id}", new { content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Delete, $"{Api}/comments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, $"{Api}/comments/{id}/hidden", new { hidden = true })).StatusCode);
        await As("classmate");
        var edited = await Json(await Expect(HttpStatusCode.OK, HttpMethod.Put, $"{Api}/comments/{id}", new { content = "Sửa lại" }));
        Assert.Equal("Sửa lại", edited.GetProperty("content").GetString());

        await As("teacher");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Put, $"{Api}/comments/{id}/hidden", new { hidden = true });
        Assert.Equal(2, (await Get($"{Api}/posts/{post}/comments")).GetProperty("total").GetInt32());
        await As("student");
        var visible = await Get($"{Api}/posts/{post}/comments");
        Assert.Equal(1, visible.GetProperty("total").GetInt32());
        Assert.DoesNotContain("Sửa lại", JsonSerializer.Serialize(visible));
        await As("classmate");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Put, $"{Api}/comments/{id}", new { content = "x" })).StatusCode);

        await As("teacher");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Delete, $"{Api}/comments/{id}");
        Assert.Equal(1, (await Get($"{Api}/posts/{post}/comments")).GetProperty("total").GetInt32());
        Assert.Equal((users["teacher"], FeedState.Removed), await Db(async db =>
        { var x = await db.Comments.SingleAsync(c => c.Id == id); return (x.ModeratedBy!.Value, x.State); }));

        // Tác giả xóa bình luận của mình.
        var own = Assert.Single(Items(await Get($"{Api}/posts/{post}/comments")));
        Assert.Equal(users["teacher"], own.GetProperty("authorId").GetGuid());
        await Expect(HttpStatusCode.NoContent, HttpMethod.Delete, $"{Api}/comments/{own.GetProperty("id").GetGuid()}");
        Assert.Equal(0, (await Get($"{Api}/posts/{post}/comments")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Feed_Likes_AreUnique_AndRepeatedUnlikeNeverGoesNegative()
    {
        await EnrollMembers();
        var post = await Post("student");
        async Task<JsonElement> Like(string name, bool like)
        {
            await As(name);
            return await Json(await Expect(HttpStatusCode.OK, like ? HttpMethod.Put : HttpMethod.Delete, $"{Api}/posts/{post}/like"));
        }

        // Unlike khi chưa like: vẫn 0.
        Assert.Equal(0, (await Like("classmate", false)).GetProperty("likeCount").GetInt32());
        Assert.Equal(1, (await Like("student", true)).GetProperty("likeCount").GetInt32());
        Assert.Equal(1, (await Like("student", true)).GetProperty("likeCount").GetInt32());
        Assert.Equal(2, (await Like("classmate", true)).GetProperty("likeCount").GetInt32());
        Assert.Equal(2, await Db(db => db.Likes.CountAsync()));
        Assert.True(Assert.Single(Items(await Posts("classmate"))).GetProperty("likedByMe").GetBoolean());
        Assert.Equal(2, Assert.Single(Items(await Posts("classmate"))).GetProperty("likeCount").GetInt32());

        Assert.Equal(1, (await Like("student", false)).GetProperty("likeCount").GetInt32());
        Assert.Equal(1, (await Like("student", false)).GetProperty("likeCount").GetInt32());
        Assert.Equal(1, (await Like("student", false)).GetProperty("likeCount").GetInt32());
        Assert.False(Assert.Single(Items(await Posts("student"))).GetProperty("likedByMe").GetBoolean());
        Assert.Equal(0, (await Like("classmate", false)).GetProperty("likeCount").GetInt32());
        Assert.Equal(0, (await Like("classmate", false)).GetProperty("likeCount").GetInt32());
        Assert.Equal(0, await Db(db => db.Likes.CountAsync()));
    }

    [Fact]
    public async Task Feed_RevokedEnrollment_LosesAccessImmediately()
    {
        await EnrollMembers();
        await Post("student");
        await As("admin");
        var membership = await RegistrationOf("student");
        await Expect(HttpStatusCode.NoContent, HttpMethod.Delete, $"/api/enrollment/memberships/{membership}");
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"{Api}/classes/{classId}/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = "x" })).StatusCode);
        await As("classmate");
        Assert.Single(Items(await Get($"{Api}/classes/{classId}/posts")));
    }

    [Fact]
    public async Task Feed_Validation_AndPagination()
    {
        await EnrollMembers();
        await As("student");
        foreach (var bad in new[] { "", "   ", new string('a', 4001), "bell\u0007" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = bad })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = new string('a', 4000) })).StatusCode);
        var post = await Post("student", "Có dòng 1\nvà dòng 2");
        foreach (var bad in new[] { "", " ", new string('a', 1001), "bell\u0007" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = bad })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, $"{Api}/posts/{post}", new { content = " " })).StatusCode);

        for (var i = 0; i < 23; i++) await Post("student", $"Bài {i}");
        var first = await Posts("student");
        Assert.Equal(25, first.GetProperty("total").GetInt32());
        Assert.Equal(20, Items(first).Count);
        Assert.Equal(5, Items(await Posts("student", query: "?page=2")).Count);
        var times = Items(first).Select(x => x.GetProperty("createdAtUtc").GetDateTime()).ToList();
        Assert.Equal(times.OrderByDescending(x => x), times);
        Assert.Equal(100, (await Posts("student", query: "?pageSize=1000")).GetProperty("pageSize").GetInt32());
        foreach (var bad in new[] { "?page=0", "?pageSize=0", "?page=1000001" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, $"{Api}/classes/{classId}/posts{bad}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, $"{Api}/posts/{post}/comments{bad}")).StatusCode);
        }
    }

    // ---------- tên tác giả bảng tin ----------

    [Fact]
    public async Task Feed_ShowsAuthorNames_ForPostsAndComments_WithoutLeakingEmail_AndNullWhenUnknown()
    {
        await EnrollMembers();
        var post = await Post("student");
        await As("classmate");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "hi" });
        await As("teacher");
        var created = await Json(await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "ok" }));
        Assert.Equal("teacher", created.GetProperty("authorName").GetString());
        await Db(async db =>
        {
            db.Posts.Add(ClassPost.Create(classId, Guid.NewGuid(), Roles.Student, "Tác giả không còn", DateTime.UtcNow.AddMinutes(1)));
            await db.SaveChangesAsync();
            return 0;
        });

        var posts = await Posts("student");
        var names = Items(posts).ToDictionary(x => x.GetProperty("id").GetGuid(), x => x.GetProperty("authorName").GetString());
        Assert.Equal("student", names[post]);
        Assert.Single(names.Values, x => x is null);
        var comments = await Get($"{Api}/posts/{post}/comments");
        Assert.Equal(new[] { "classmate", "teacher" }, Items(comments).Select(x => x.GetProperty("authorName").GetString()!).Order().ToArray());
        foreach (var raw in new[] { JsonSerializer.Serialize(Items(posts)), JsonSerializer.Serialize(Items(comments)), created.ToString() })
        {
            Assert.DoesNotContain("@example.test", raw);
            Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- thông báo điểm danh và bài bỏ ----------

    [Fact]
    public async Task AttendanceAndMissedAssignment_NotifyOnlyTheStudent_ReplayDoesNotDuplicate_AndCarryNoStatus()
    {
        await Enroll("student");
        var (attendance, session, assignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var student = users["student"];
        var first = new AttendanceRevisionRecordedIntegrationEvent(attendance, session, classId, student, "AbsentUnexcused", 1, users["teacher"], DateTime.UtcNow);
        await Publish(first);
        await Publish(first with { EventId = Guid.NewGuid() });
        await Publish(first with { Revision = 2, Status = "Present" });
        var missed = new AssignmentMissedIntegrationEvent(assignment, classId, student);
        await Publish(missed);
        await Publish(missed with { EventId = Guid.NewGuid() });

        await As("student");
        var items = Items(await Inbox()).Where(x => x.GetProperty("kind").GetString() != "StudentEnrolled").ToList();
        Assert.Equal(new[] { "AssignmentMissed", "AttendanceRecorded", "AttendanceRecorded" }, items.Select(x => x.GetProperty("kind").GetString()!).Order().ToArray());
        Assert.All(items.Where(x => x.GetProperty("kind").GetString() == "AttendanceRecorded"), x =>
        {
            Assert.Equal("Session", x.GetProperty("targetType").GetString());
            Assert.Equal(session, x.GetProperty("targetId").GetGuid());
        });
        var due = items.Single(x => x.GetProperty("kind").GetString() == "AssignmentMissed");
        Assert.Equal("Assignment", due.GetProperty("targetType").GetString());
        Assert.Equal(assignment, due.GetProperty("targetId").GetGuid());
        var raw = JsonSerializer.Serialize(items);
        foreach (var forbidden in new[] { "Absent", "Present", "Unexcused" })
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { "classmate", "teacher", "staff" })
        {
            await As(name);
            Assert.Equal(0, (await Inbox()).GetProperty("total").GetInt32());
        }
    }

    // ---------- giới hạn tần suất ----------

    private async Task AssertTooManyRequests(HttpMethod method, string url, object body)
    {
        var response = await Send(method, url, body);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Feedback_IsLimitedToFivePerHourPerStudent_AndAllowedAgainAfterAnHour()
    {
        await As("student");
        for (var i = 0; i < 5; i++) await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/feedback", Feedback());
        // Dữ liệu sai bị từ chối 400 trước và không tính vào giới hạn.
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Api}/feedback", Feedback(title: ""))).StatusCode);
        await AssertTooManyRequests(HttpMethod.Post, $"{Api}/feedback", Feedback());
        Assert.Equal(5, await Db(db => db.Feedbacks.CountAsync()));

        await As("classmate");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/feedback", Feedback());

        factory.Clock.Shift = TimeSpan.FromMinutes(61);
        await As("student");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/feedback", Feedback());
    }

    [Fact]
    public async Task FeedPostsAndComments_AreLimitedTo30PerHourPerUser_AndAllowedAgainAfterAnHour()
    {
        await EnrollMembers();
        var post = await Post("classmate");
        await As("student");
        for (var i = 0; i < 20; i++) await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = $"Bài {i}" });
        for (var i = 0; i < 10; i++) await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = $"Bình luận {i}" });
        await AssertTooManyRequests(HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = "thừa" });
        await AssertTooManyRequests(HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "thừa" });
        // Sửa, thích và đọc không bị giới hạn.
        await Expect(HttpStatusCode.OK, HttpMethod.Put, $"{Api}/posts/{post}/like");
        Assert.Equal(30, await Db(async db => await db.Posts.CountAsync(x => x.AuthorId == users["student"]) +
            await db.Comments.CountAsync(x => x.AuthorId == users["student"])));

        await As("classmate");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = "vẫn được" });

        factory.Clock.Shift = TimeSpan.FromMinutes(61);
        await As("student");
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/classes/{classId}/posts", new { content = "sau một giờ" });
        await Expect(HttpStatusCode.Created, HttpMethod.Post, $"{Api}/posts/{post}/comments", new { content = "sau một giờ" });
    }

    private sealed class ShiftClock : TimeProvider
    {
        public TimeSpan Shift { get; set; }
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Shift;
    }

    private sealed class Tv4Factory : WebApplicationFactory<Program>
    {
        private readonly List<SqliteConnection> connections = [];
        public ShiftClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Microsoft"] = "Warning",
                ["Identity:InitializeDatabase"] = "false", ["Academic:InitializeDatabase"] = "false",
                ["Enrollment:InitializeDatabase"] = "false", ["MediaNotification:InitializeDatabase"] = "false",
                ["Finance:InitializeDatabase"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                Sqlite<IdentityDbContext>(services); Sqlite<AcademicDbContext>(services);
                Sqlite<EnrollmentDbContext>(services); Sqlite<MediaNotificationDbContext>(services);
                Sqlite<FinanceDbContext>(services);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }

        private void Sqlite<T>(IServiceCollection services) where T : DbContext
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            connections.Add(connection);
            services.RemoveAll<DbContextOptions<T>>();
            services.RemoveAll<IDbContextOptionsConfiguration<T>>();
            services.AddDbContext<T>(o => o.UseSqlite(connection));
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            foreach (var connection in connections) await connection.DisposeAsync();
        }
    }
}

