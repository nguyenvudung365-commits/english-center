using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.Security;
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

// Tài liệu lớp độc lập + bài thi dạng file (đề PDF do giáo viên tải lên) tái sử dụng nộp bài/chấm điểm của coursework.
public sealed class ClassMaterialsAndExamsTests : IAsyncLifetime
{
    private const string Password = "Materials-test-123!";
    private static readonly string[] Students = ["student", "other-student"];
    private readonly Factory factory = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, Guid> users = [];
    private Guid classId, otherClassId, classSessionId;

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
        var accounts = new[] { ("admin", Roles.Admin), ("teacher", Roles.Teacher), ("other-teacher", Roles.Teacher) }
            .Concat(Students.Select(x => (x, Roles.Student)));
        foreach (var (name, role) in accounts)
        {
            var user = User.Create($"{name}@example.test", name, role, passwords.Hash(Password), DateTimeOffset.UtcNow);
            identity.Users.Add(user);
            users[name] = user.Id;
            if (role == Roles.Teacher) academic.Teachers.Add(Teacher.Create(user.Id, $"GV {name}", user.Email));
        }
        await identity.SaveChangesAsync();
        var course = Course.Create(Guid.NewGuid(), "MAT", "Materials");
        academic.Courses.Add(course);
        var item = Class.Create("MAT-01", "Lớp tài liệu", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 12, 31), 20, 0,
            users["teacher"], DateTimeOffset.UtcNow);
        var other = Class.Create("MAT-02", "Lớp khác", course.Id, ClassType.Main, new(2026, 1, 1), new(2027, 12, 31), 20, 0,
            users["other-teacher"], DateTimeOffset.UtcNow);
        academic.Classes.AddRange(item, other);
        classId = item.Id;
        otherClassId = other.Id;
        await academic.SaveChangesAsync();
        var enrollment = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        foreach (var name in Students) enrollment.Students.Add(new Student(users[name], name, $"{name}@example.test"));
        enrollment.Classes.Add(new ClassSnapshot(item.Id, item.Code, item.StartDate, item.EndDate, item.MaxStudents, item.TuitionFee));
        enrollment.Classes.Add(new ClassSnapshot(other.Id, other.Code, other.StartDate, other.EndDate, other.MaxStudents, other.TuitionFee));
        await enrollment.SaveChangesAsync();
        classSessionId = await CourseworkSessions.SeedAsync(factory.Services, classId, "Unit 4", "Lesson 1");
    }

    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }

    // Đăng nhập có giới hạn tần suất (429): mỗi tài khoản đăng nhập một lần rồi dùng lại token.
    private readonly Dictionary<string, string> tokens = [];

    private async Task As(string name)
    {
        client.DefaultRequestHeaders.Authorization = null;
        if (!tokens.TryGetValue(name, out var access))
        {
            var response = await client.PostAsJsonAsync("/api/identity/auth/login", new { email = $"{name}@example.test", password = Password });
            response.EnsureSuccessStatusCode();
            tokens[name] = access = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
    }

    private async Task<Guid> Enroll(string student = "student")
    {
        await As("admin");
        var registered = await client.PostAsJsonAsync("/api/enrollment/enrollments", new
        { classId, studentId = users[student], maxAbsences = 3, maxLates = 5, maxSkippedAssignments = 2 });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var response = await client.PostAsJsonAsync("/api/enrollment/memberships", new { classId, studentId = users[student] });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> File(string owner, string purpose, bool confirmed = true, string extension = "pdf")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>();
        var file = StoredFile.Create(users[owner], $"{Guid.NewGuid():N}.{extension}", "application/pdf", 1024, purpose,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        if (confirmed) file.Complete($"files/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<HttpResponseMessage> CreateMaterial(string user, Guid? fileId, string title = "Tài liệu Unit 4", Guid? inClass = null)
    {
        await As(user);
        return await client.PostAsJsonAsync("/api/academic/materials",
            new { classId = inClass ?? classId, title, description = "Đọc trước buổi học", fileId });
    }

    private async Task<Guid> PublishMaterial(string title = "Tài liệu Unit 4")
    {
        var response = await CreateMaterial("teacher", await File("teacher", "material"), title);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> CreateExam(Guid? prompt, double opensHours = -1, double closesHours = 2, bool? publish = null,
        string? kind = "Exam", string user = "teacher")
    {
        await As(user);
        var now = DateTimeOffset.UtcNow;
        return await client.PostAsJsonAsync("/api/academic/assignments", new
        {
            classId, title = "Thi giữa kỳ", description = "Làm bài trong giờ", opensAtUtc = now.AddHours(opensHours),
            closesAtUtc = now.AddHours(closesHours), kind, publish, sessionId = classSessionId, promptFileId = prompt
        });
    }

    private async Task<Guid> PublishedExam(double opensHours = -1, double closesHours = 2, bool? publish = null)
    {
        var response = await CreateExam(await File("teacher", "exam"), opensHours, closesHours, publish);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    private async Task<int> MaterialCount(string user)
    {
        await As(user);
        return (await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/materials?classId={classId}"))!.Length;
    }

    private async Task<HttpStatusCode> PromptStatus(string user, Guid assignment)
    {
        await As(user);
        return (await client.GetAsync($"/api/academic/assignments/{assignment}/prompt/download")).StatusCode;
    }

    // ---- Materials ----

    [Fact]
    public async Task Teacher_PublishesMaterial_ActiveMembersListAndDownload()
    {
        await Enroll();
        var id = await PublishMaterial();

        await As("student");
        var list = (await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/materials?classId={classId}"))!;
        var material = Assert.Single(list);
        Assert.Equal(id, material.GetProperty("id").GetGuid());
        Assert.Equal("Tài liệu Unit 4", material.GetProperty("title").GetString());
        Assert.EndsWith(".pdf", material.GetProperty("fileName").GetString());
        Assert.Equal(users["teacher"], material.GetProperty("uploadedBy").GetGuid());
        foreach (var user in new[] { "student", "teacher", "admin" })
        {
            await As(user);
            var download = await client.GetAsync($"/api/academic/materials/{id}/download");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.StartsWith("https://storage.example/files/", (await Json(download)).GetProperty("url").GetString());
        }
    }

    [Fact]
    public async Task Material_OutsidersAndWrongRoles_AreRejected()
    {
        await Enroll();
        var id = await PublishMaterial();

        // Học viên không có quyền lớp, giáo viên lớp khác: không xem, không tải, giáo viên lớp khác không đăng.
        foreach (var user in new[] { "other-student", "other-teacher" })
        {
            await As(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/materials?classId={classId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/materials/{id}/download")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await CreateMaterial("other-teacher", await File("other-teacher", "material"))).StatusCode);
        // Học viên đúng lớp cũng không đăng được.
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateMaterial("student", await File("student", "material"))).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/academic/materials?classId={classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/academic/materials/{id}/download")).StatusCode);
        Assert.Equal(1, await MaterialCount("teacher"));
    }

    [Fact]
    public async Task Material_RevokedMembership_LosesListAndDownload()
    {
        var membership = await Enroll();
        var id = await PublishMaterial();
        await As("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/enrollment/memberships/{membership}")).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/materials?classId={classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/materials/{id}/download")).StatusCode);
    }

    [Theory]
    [InlineData("foreign-owner")]
    [InlineData("submission-purpose")]
    [InlineData("exam-purpose")]
    [InlineData("unconfirmed")]
    [InlineData("unknown")]
    [InlineData("no-file")]
    [InlineData("blank-title")]
    public async Task Material_RejectsFilesNotOwnedConfirmedOrIntendedForMaterials(string kind)
    {
        Guid? file = kind switch
        {
            "foreign-owner" => await File("other-teacher", "material"),
            "submission-purpose" => await File("teacher", "submission"),
            "exam-purpose" => await File("teacher", "exam"),
            "unconfirmed" => await File("teacher", "material", confirmed: false),
            "unknown" => Guid.NewGuid(),
            "no-file" => null,
            _ => await File("teacher", "material")
        };
        var response = await CreateMaterial("teacher", file, kind == "blank-title" ? "  " : "Tài liệu");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await MaterialCount("teacher"));
    }

    [Fact]
    public async Task Material_SameFileCannotBePublishedTwice_UnknownClassAndMaterialAre404()
    {
        var file = await File("teacher", "material");
        Assert.Equal(HttpStatusCode.Created, (await CreateMaterial("teacher", file)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreateMaterial("teacher", file)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateMaterial("admin", await File("admin", "material"), inClass: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/materials/{Guid.NewGuid()}/download")).StatusCode);
        Assert.Equal(1, await MaterialCount("teacher"));
    }

    // ---- File-based exams ----

    [Fact]
    public async Task Practice_TextSubmissionPersists_ExamStillRequiresFiles()
    {
        await Enroll();
        var exam = await PublishedExam();
        var practice = (await Json(await CreateExam(null, kind: "Homework"))).GetProperty("id").GetGuid();
        var path = $"/api/academic/assignments/{practice}/submissions";
        await As("other-student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, new { answerText = "Outside class" })).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/academic/assignments/{exam}/submissions", new { answerText = "Text only" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new { answerText = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new { answerText = new string('a', 10001) })).StatusCode);
        const string answerText = "1. The library opens at nine.\n2. Students can borrow two books.";
        var submitted = await client.PutAsJsonAsync(path, new { answerText });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var saved = await Json(submitted);
        var revision = saved.GetProperty("revision").GetGuid();
        Assert.Equal(revision, (await Json(await client.PutAsJsonAsync(path, new { answerText }))).GetProperty("revision").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new { answerText = "Stale edit" })).StatusCode);
        Assert.Equal(answerText, (await client.GetFromJsonAsync<JsonElement>(path + "/mine")).GetProperty("answerText").GetString());
        await As("teacher");
        var work = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>(path))!);
        Assert.Equal(answerText, work.GetProperty("answerText").GetString());
        Assert.Empty(work.GetProperty("files").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/academic/submissions/{saved.GetProperty("id").GetGuid()}/grade",
            new { score = 8m, feedback = "Tốt", expectedGradeVersion = 0, expectedRevision = revision })).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new { answerText = "After grading", expectedRevision = revision })).StatusCode);
        Assert.Equal(8m, (await client.GetFromJsonAsync<JsonElement>(path + "/mine")).GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task Exam_TeacherUploadsPrompt_StudentDownloadsSubmitsFiles_TeacherGrades()
    {
        var enrollment = await Enroll();
        var created = await CreateExam(await File("teacher", "exam"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var exam = await Json(created);
        var id = exam.GetProperty("id").GetGuid();
        Assert.Equal("Exam", exam.GetProperty("kind").GetString());
        Assert.EndsWith(".pdf", exam.GetProperty("promptFileName").GetString());

        await As("student");
        var prompt = await client.GetAsync($"/api/academic/assignments/{id}/prompt/download");
        Assert.Equal(HttpStatusCode.OK, prompt.StatusCode);
        Assert.StartsWith("https://storage.example/files/", (await Json(prompt)).GetProperty("url").GetString());

        var answer = await File("student", "submission");
        var submitted = await client.PutAsJsonAsync($"/api/academic/assignments/{id}/submissions", new { fileIds = new[] { answer } });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var submission = await Json(submitted);

        await As("teacher");
        var graded = await client.PutAsJsonAsync($"/api/academic/submissions/{submission.GetProperty("id").GetGuid()}/grade", new
        { score = 7.5m, feedback = "Tốt", expectedGradeVersion = 0, expectedRevision = submission.GetProperty("revision").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, graded.StatusCode);
        await As("student");
        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/{id}/submissions/mine");
        Assert.Equal(7.5m, mine.GetProperty("score").GetDecimal());

        await As("admin");
        using var transfer = await client.PostAsJsonAsync($"/api/enrollment/enrollments/{enrollment}/transfer", new { targetClassId = otherClassId });
        Assert.Equal(HttpStatusCode.Created, transfer.StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, await PromptStatus("student", id));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/academic/assignments/{id}/submissions", new { fileIds = new[] { answer } })).StatusCode);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine");
        var row = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.True(row.GetProperty("isTransferredHistory").GetBoolean());
        Assert.Equal(7.5m, row.GetProperty("score").GetDecimal());
        Assert.Equal("Tốt", row.GetProperty("feedback").GetString());
        Assert.Equal("", row.GetProperty("assignment").GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("assignment").GetProperty("promptFileName").ValueKind);
        await As("other-student");
        var outsider = await client.GetFromJsonAsync<JsonElement>("/api/academic/assignments/mine");
        Assert.Empty(outsider.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ExamPrompt_StudentsDownloadOnlyWhileOpen_ManagersAnytime_OutsidersNever()
    {
        await Enroll();
        var open = await PublishedExam();
        var upcoming = await PublishedExam(opensHours: 2, closesHours: 4);
        var closed = await PublishedExam(opensHours: -4, closesHours: -2);
        var draft = await PublishedExam(publish: false);

        Assert.Equal(HttpStatusCode.OK, await PromptStatus("student", open));
        Assert.Equal(HttpStatusCode.Forbidden, await PromptStatus("student", upcoming));
        Assert.Equal(HttpStatusCode.Forbidden, await PromptStatus("student", closed));
        Assert.Equal(HttpStatusCode.NotFound, await PromptStatus("student", draft));
        foreach (var exam in new[] { open, upcoming, closed, draft })
        {
            Assert.Equal(HttpStatusCode.OK, await PromptStatus("teacher", exam));
            Assert.Equal(HttpStatusCode.OK, await PromptStatus("admin", exam));
            Assert.Equal(HttpStatusCode.Forbidden, await PromptStatus("other-student", exam));
            Assert.Equal(HttpStatusCode.Forbidden, await PromptStatus("other-teacher", exam));
        }
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/academic/assignments/{open}/prompt/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await PromptStatus("teacher", Guid.NewGuid()));
    }

    [Fact]
    public async Task ExamPrompt_RevokedMember_CannotDownload()
    {
        var membership = await Enroll();
        var exam = await PublishedExam();
        await As("admin");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/enrollment/memberships/{membership}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await PromptStatus("student", exam));
    }

    [Theory]
    [InlineData("exam-without-prompt")]
    [InlineData("homework-with-prompt")]
    [InlineData("foreign-owner")]
    [InlineData("material-purpose")]
    [InlineData("submission-purpose")]
    [InlineData("unconfirmed")]
    public async Task Exam_PromptRules_RejectedBeforeAnythingIsCreated(string kind)
    {
        var (prompt, examKind) = kind switch
        {
            "exam-without-prompt" => ((Guid?)null, "Exam"),
            "homework-with-prompt" => (await File("teacher", "exam"), "Homework"),
            "foreign-owner" => (await File("other-teacher", "exam"), "Exam"),
            "material-purpose" => (await File("teacher", "material"), "Exam"),
            "submission-purpose" => (await File("teacher", "submission"), "Exam"),
            _ => (await File("teacher", "exam", confirmed: false), "Exam")
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateExam(prompt, kind: examKind)).StatusCode);
        await As("teacher");
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments?classId={classId}"))!);
    }

    [Fact]
    public async Task Exam_PromptCanChangeWhileDraft_ButNotAfterPublish()
    {
        var id = await PublishedExam(publish: false);
        var second = await File("teacher", "exam");
        var third = await File("teacher", "exam");
        var now = DateTimeOffset.UtcNow;
        async Task<HttpResponseMessage> Update(Guid? prompt, int version, string title = "Thi giữa kỳ (sửa)") =>
            await client.PutAsJsonAsync($"/api/academic/assignments/{id}", new
            {
                title, description = "", opensAtUtc = now.AddHours(-1), closesAtUtc = now.AddHours(2), kind = "Exam",
                sessionId = classSessionId, expectedVersion = version, promptFileId = prompt
            });
        await As("teacher");
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/{id}");
        var firstName = current.GetProperty("promptFileName").GetString();

        var replaced = await Update(second, current.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var afterReplace = await Json(replaced);
        Assert.NotEqual(firstName, afterReplace.GetProperty("promptFileName").GetString());

        // Không gửi promptFileId: giữ đề hiện tại.
        var kept = await Update(null, afterReplace.GetProperty("version").GetInt32(), "Chỉ đổi tiêu đề");
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        var keptBody = await Json(kept);
        Assert.Equal(afterReplace.GetProperty("promptFileName").GetString(), keptBody.GetProperty("promptFileName").GetString());

        var published = await client.PostAsJsonAsync($"/api/academic/assignments/{id}/publish",
            new { expectedVersion = keptBody.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var version = (await Json(published)).GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await Update(third, version)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Update(null, version, "Đổi chữ sau công bố")).StatusCode);
        // Đã công bố: không chạm tới file mới (kể cả file của người khác), trả 409 trước khi tra cứu.
        Assert.Equal(HttpStatusCode.Conflict, (await Update(await File("other-teacher", "exam"), version + 1)).StatusCode);
    }

    [Fact]
    public async Task PromptDownload_OnAssignmentWithoutPrompt_Is404()
    {
        await Enroll();
        await As("teacher");
        var now = DateTimeOffset.UtcNow;
        var created = await client.PostAsJsonAsync("/api/academic/assignments", new
        {
            classId, title = "Bài thường", opensAtUtc = now.AddHours(-1), closesAtUtc = now.AddHours(2), sessionId = classSessionId
        });
        var id = (await Json(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, await PromptStatus("student", id));
        Assert.Equal(HttpStatusCode.NotFound, await PromptStatus("teacher", id));
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly List<SqliteConnection> connections = [];

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
                services.RemoveAll<IObjectStorage>(); services.AddSingleton<IObjectStorage, SigningStorage>();
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

    private sealed class SigningStorage : IObjectStorage
    {
        public Task<Uri> PresignGetAsync(string key, string name, DateTimeOffset expiresAt, CancellationToken ct) => Task.FromResult(new Uri("https://storage.example/" + key));
        public Task<Uri> PresignPutAsync(string key, string type, DateTimeOffset expiresAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task CopyAsync(string source, string destination, string etag, string type, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
