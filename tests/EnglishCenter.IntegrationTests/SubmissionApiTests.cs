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
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
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

public sealed class SubmissionApiTests : IAsyncLifetime
{
    private const string Password = "Submission-test-123!";
    private readonly SubmissionFactory factory = new();
    private HttpClient client = null!;
    private readonly Dictionary<string, Guid> users = [];
    private Guid classId;

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
        foreach (var (name, role) in new[] { ("admin", Roles.Admin), ("staff", Roles.Staff), ("teacher", Roles.Teacher),
                     ("other-teacher", Roles.Teacher), ("student", Roles.Student), ("other-student", Roles.Student) })
        {
            var user = User.Create($"{name}@example.test", name, role, passwords.Hash(Password), DateTimeOffset.UtcNow);
            identity.Users.Add(user);
            users[name] = user.Id;
            if (role == Roles.Teacher) academic.Teachers.Add(Teacher.Create(user.Id, name, user.Email));
        }
        await identity.SaveChangesAsync();
        var course = Course.Create(Guid.NewGuid(), "MULTIFILE", "Multi file");
        academic.Courses.Add(course);
        var item = Class.Create("MF-01", "Multi file", course.Id, ClassType.Main, new(2026, 1, 1),
            new(2027, 12, 31), 20, 0, users["teacher"], DateTimeOffset.UtcNow);
        academic.Classes.Add(item);
        classId = item.Id;
        await academic.SaveChangesAsync();
        var enrollment = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        foreach (var name in new[] { "student", "other-student" })
            enrollment.Students.Add(new Student(users[name], name, $"{name}@example.test"));
        enrollment.Classes.Add(new ClassSnapshot(item.Id, item.Code, item.StartDate, item.EndDate, item.MaxStudents, item.TuitionFee));
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

    private async Task<Guid> Enroll()
    {
        await As("admin");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            if (!await db.Enrollments.AnyAsync(x => x.ClassId == classId && x.StudentId == users["student"]))
            {
                var registered = await client.PostAsJsonAsync("/api/enrollment/enrollments", new
                {
                    classId, studentId = users["student"], maxAbsences = 3, maxLates = 5, maxSkippedAssignments = 2
                });
                Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            }
        }
        var response = await client.PostAsJsonAsync("/api/enrollment/memberships", new { classId, studentId = users["student"] });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> Assignment(bool expired = false)
    {
        await Enroll();
        await As("teacher");
        var now = DateTimeOffset.UtcNow;
        var response = await client.PostAsJsonAsync("/api/academic/assignments", new
        {
            classId, title = "Nộp bài nhiều file", description = "PDF và hình ảnh",
            opensAtUtc = now.AddHours(-2), closesAtUtc = expired ? now.AddHours(-1) : now.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> File(string owner = "student", bool confirmed = true, long size = 1024)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>();
        var file = StoredFile.Create(users[owner], $"{Guid.NewGuid():N}.pdf", "application/pdf", size,
            "submission", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        if (confirmed) file.Complete($"files/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private Task<HttpResponseMessage> Submit(Guid assignment, Guid[] files, Guid? revision = null) =>
        client.PutAsJsonAsync($"/api/academic/assignments/{assignment}/submissions", new { fileIds = files, expectedRevision = revision });
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task ThreeConfirmedFiles_AreOneSubmission_ReadableAndDownloadableByAssignedTeacher()
    {
        var assignment = await Assignment();
        var files = new[] { await File(), await File(), await File() };
        await As("student");
        var response = await Submit(assignment, files);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Json(response);
        Assert.Equal(users["student"], result.GetProperty("studentId").GetGuid());
        Assert.Equal(files.Order(), result.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("fileId").GetGuid()).Order());
        var mine = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/{assignment}/submissions/mine");
        Assert.Equal(result.GetProperty("id").GetGuid(), mine.GetProperty("id").GetGuid());
        await As("teacher");
        var all = await client.GetFromJsonAsync<JsonElement[]>($"/api/academic/assignments/{assignment}/submissions");
        Assert.Single(all!);
        var download = await client.GetAsync($"/api/academic/submissions/{result.GetProperty("id").GetGuid()}/files/{files[0]}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.StartsWith("https://storage.example/", (await Json(download)).GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("six")]
    [InlineData("total")]
    [InlineData("oversized")]
    [InlineData("pending")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public async Task InvalidFileSet_IsRejectedWithoutPartialSubmission(string kind)
    {
        var assignment = await Assignment();
        var valid = await File();
        Guid[] files = kind switch
        {
            "duplicate" => [valid, valid], "empty" => [], "zero" => [Guid.Empty],
            "six" => [await File(), await File(), await File(), await File(), await File(), await File()],
            "total" => [await File(size: 20 * 1024 * 1024), await File(size: 20 * 1024 * 1024), await File(size: 20 * 1024 * 1024)],
            "oversized" => [await File(size: 20 * 1024 * 1024 + 1)],
            "pending" => [valid, await File(confirmed: false)], "foreign" => [valid, await File("other-student")],
            _ => [valid, Guid.NewGuid()]
        };
        await As("student");
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(assignment, files)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/assignments/{assignment}/submissions/mine")).StatusCode);
    }

    [Fact]
    public async Task Replacement_IsAtomic_AndStaleRevisionCannotOverwrite()
    {
        var assignment = await Assignment();
        var first = await File(); var second = await File();
        await As("student");
        var original = await Json(await Submit(assignment, [first]));
        var revision = original.GetProperty("revision").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(assignment, [second, Guid.NewGuid()], revision)).StatusCode);
        var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/academic/assignments/{assignment}/submissions/mine");
        Assert.Equal(revision, unchanged.GetProperty("revision").GetGuid());
        var response = await Submit(assignment, [first, second], revision);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replaced = await Json(response);
        Assert.Equal(original.GetProperty("id").GetGuid(), replaced.GetProperty("id").GetGuid());
        Assert.Equal(2, replaced.GetProperty("files").GetArrayLength());
        Assert.NotEqual(revision, replaced.GetProperty("revision").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(assignment, [first], revision)).StatusCode);
        // Retrying the exact request returns the accepted result, not a duplicate row.
        Assert.Equal(HttpStatusCode.OK, (await Submit(assignment, [second, first], revision)).StatusCode);
    }

    [Fact]
    public async Task RevocationAndWrongRoles_AreCheckedOnEveryReadAndWrite()
    {
        var assignment = await Assignment(); var file = await File();
        await As("other-student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(assignment, [file])).StatusCode);
        await As("student");
        var result = await Json(await Submit(assignment, [file]));
        var path = $"/api/academic/submissions/{result.GetProperty("id").GetGuid()}/files/{file}/download";
        await As("other-teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments/{assignment}/submissions")).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/enrollment/memberships", new { classId, studentId = users["student"] })).StatusCode);
        await As("admin");
        var enrollments = await client.GetFromJsonAsync<JsonElement[]>($"/api/enrollment/memberships?classId={classId}");
        var id = enrollments!.Single().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/enrollment/memberships/{id}")).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(assignment, [file])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task ExpiredAssignment_RejectsEvenConfirmedFiles()
    {
        var assignment = await Assignment(expired: true); var file = await File();
        await As("student");
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(assignment, [file])).StatusCode);
    }

    [Fact]
    public async Task Download_CannotSubstituteAnotherFileId()
    {
        var assignment = await Assignment(); var file = await File(); var unattached = await File();
        await As("student");
        var result = await Json(await Submit(assignment, [file]));
        await As("teacher");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/submissions/{result.GetProperty("id").GetGuid()}/files/{unattached}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/media-notification/files/{file}/download")).StatusCode);
    }

    [Theory]
    [InlineData("missing-class", HttpStatusCode.NotFound)]
    [InlineData("teacher-account", HttpStatusCode.BadRequest)]
    [InlineData("unknown-student", HttpStatusCode.BadRequest)]
    [InlineData("empty-student", HttpStatusCode.BadRequest)]
    [InlineData("locked-student", HttpStatusCode.BadRequest)]
    public async Task Membership_ValidatesExistingClassAndActiveStudent(string kind, HttpStatusCode expected)
    {
        await As("admin");
        if (kind == "locked-student")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.SingleAsync(x => x.Id == users["student"])).SetLocked(true);
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync("/api/enrollment/memberships", new
        {
            classId = kind == "missing-class" ? Guid.NewGuid() : classId,
            studentId = kind switch { "teacher-account" => users["teacher"], "unknown-student" => Guid.NewGuid(), "empty-student" => Guid.Empty, _ => users["student"] }
        });
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>($"/api/enrollment/memberships?classId={classId}"))!);
    }

    [Fact]
    public async Task Membership_CreateRetryAndReactivationReuseSameRow()
    {
        var id = await Enroll();
        Assert.Equal(id, await Enroll());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/enrollment/memberships/{id}")).StatusCode);
        Assert.Equal(id, await Enroll());
        Assert.True(Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/enrollment/memberships?classId={classId}"))!).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task AssignmentCreation_RequiresAssignedTeacherOrManagerAndValidWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new { classId, title = "Work", description = "", opensAtUtc = now.AddHours(-1), closesAtUtc = now.AddHours(1) };
        await As("other-teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/academic/assignments", request)).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/academic/assignments", request)).StatusCode);
        await As("teacher");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/academic/assignments", new { classId, title = "Work", opensAtUtc = now, closesAtUtc = now })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/academic/assignments", request)).StatusCode);
    }

    [Fact]
    public async Task FutureAssignment_IsReadableButCannotBeSubmittedBeforeOpening()
    {
        await Enroll(); await As("teacher");
        var now = DateTimeOffset.UtcNow;
        var created = await client.PostAsJsonAsync("/api/academic/assignments", new { classId, title = "Future", opensAtUtc = now.AddHours(1), closesAtUtc = now.AddHours(2) });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetGuid();
        var file = await File(); await As("student");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/academic/assignments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(id, [file])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/assignments/{id}/submissions/mine")).StatusCode);
    }

    [Fact]
    public async Task Revocation_BlocksAssignmentAndSubmissionReadsWithoutDependingOnEventDelivery()
    {
        var id = await Assignment(); var file = await File(); await As("student");
        Assert.Equal(HttpStatusCode.OK, (await Submit(id, [file])).StatusCode);
        await As("admin");
        var membership = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/enrollment/memberships?classId={classId}"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/enrollment/memberships/{membership.GetProperty("id").GetGuid()}")).StatusCode);
        await As("student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments?classId={classId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments/{id}/submissions/mine")).StatusCode);
    }

    [Fact]
    public async Task FirstSubmission_WithInventedRevision_IsConflictAndDoesNotCreateRow()
    {
        var id = await Assignment(); var file = await File(); await As("student");
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(id, [file], Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/academic/assignments/{id}/submissions/mine")).StatusCode);
    }

    [Theory]
    [InlineData("teacher", false)]
    [InlineData("admin", false)]
    [InlineData("staff", false)]
    [InlineData("teacher", true)]
    [InlineData("admin", true)]
    [InlineData("staff", true)]
    public async Task AccountRevocation_RejectsExistingTokenForAcademicAndRoster(string actor, bool changeRole)
    {
        var assignment = await Assignment(); var file = await File();
        await As("student");
        var submission = await Json(await Submit(assignment, [file]));
        await As("admin");
        var membership = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/enrollment/memberships?classId={classId}"))!);
        var rosterId = membership.GetProperty("id").GetGuid();
        await As(actor);
        var token = client.DefaultRequestHeaders.Authorization;
        var listPath = $"/api/academic/assignments/{assignment}/submissions";
        var downloadPath = $"/api/academic/submissions/{submission.GetProperty("id").GetGuid()}/files/{file}/download";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(listPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(downloadPath)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == users[actor]);
            if (changeRole) db.Entry(user).Property(x => x.Role).CurrentValue = Roles.Student;
            else user.SetLocked(true);
            await db.SaveChangesAsync();
        }
        // Reuse the authenticated token: no second login or token refresh hides the revocation.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(listPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(downloadPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/academic/assignments/{assignment}")).StatusCode);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/academic/assignments", new
        { classId, title = "Revoked", opensAtUtc = now, closesAtUtc = now.AddHours(1) })).StatusCode);
        if (actor != "teacher")
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/enrollment/memberships?classId={classId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/enrollment/memberships/{rosterId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/enrollment/memberships", new
            { classId, studentId = users["other-student"] })).StatusCode);
        }
        Assert.Equal(token, client.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("staff", false, false)]
    [InlineData("admin", false, false)]
    [InlineData("staff", true, false)]
    [InlineData("admin", true, false)]
    [InlineData("staff", false, true)]
    [InlineData("admin", false, true)]
    [InlineData("staff", true, true)]
    [InlineData("admin", true, true)]
    public async Task AccountRevocation_RejectsExistingTokenForFinancialEnrollmentAndReplay_WithoutMutation(string actor, bool changeRole, bool replay)
    {
        Guid? registrationId = replay ? await Enroll() : null;
        Guid? invoiceId = null;
        if (replay)
        {
            using var scope = factory.Services.CreateScope();
            invoiceId = (await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.SingleAsync()).Id;
        }
        await As(actor);
        var token = client.DefaultRequestHeaders.Authorization;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == users[actor]);
            if (changeRole) db.Entry(user).Property(x => x.Role).CurrentValue = Roles.Student;
            else user.SetLocked(true);
            await db.SaveChangesAsync();
        }

        var response = replay
            ? await client.PostAsync($"/api/enrollment/enrollments/{registrationId}/replay", null)
            : await client.PostAsJsonAsync("/api/enrollment/enrollments", new
            {
                classId, studentId = users["student"], maxAbsences = 3, maxLates = 5, maxSkippedAssignments = 2
            });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(token, client.DefaultRequestHeaders.Authorization);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            var count = replay ? 1 : 0;
            Assert.Equal(count, await db.Enrollments.CountAsync());
            Assert.Equal(count, (await db.Classes.SingleAsync()).CurrentStudents);
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            Assert.Equal(count, await finance.Invoices.CountAsync());
            Assert.Equal(count, factory.EnrollmentEvents.Count);
            Assert.Equal(count, factory.InvoiceEvents.Count);
            if (replay)
            {
                var stored = await db.Enrollments.SingleAsync();
                Assert.Equal(registrationId, stored.Id);
                Assert.Equal("Active", stored.Status);
                Assert.True(stored.IsActive);
                Assert.Equal((3, 5, 2), (stored.MaxAbsences, stored.MaxLates, stored.MaxSkippedAssignments));
                Assert.Equal(invoiceId, (await finance.Invoices.SingleAsync()).Id);
            }
        }
    }

    [Theory]
    [InlineData("staff", false, false)]
    [InlineData("admin", false, false)]
    [InlineData("staff", true, false)]
    [InlineData("admin", true, false)]
    [InlineData("staff", false, true)]
    [InlineData("admin", false, true)]
    [InlineData("staff", true, true)]
    [InlineData("admin", true, true)]
    [InlineData("student", false, false)]
    [InlineData("student", true, false)]
    [InlineData("student", false, true)]
    [InlineData("student", true, true)]
    public async Task AccountRevocation_RejectsExistingTokenForFinancialReads(string actor, bool changeRole, bool invoice)
    {
        var registrationId = await Enroll();
        var path = invoice
            ? $"/api/finance/invoices/by-enrollment/{registrationId}"
            : $"/api/enrollment/enrollments/{registrationId}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            Assert.Equal(users["student"], (await db.Enrollments.SingleAsync()).StudentId);
            Assert.Single(await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Invoices.ToListAsync());
        }
        await As("student");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        await As("other-student");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        await As(actor);
        var token = client.DefaultRequestHeaders.Authorization;
        Assert.NotNull(token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == users[actor]);
            if (changeRole)
                db.Entry(user).Property(x => x.Role).CurrentValue = actor == "student" ? Roles.Teacher : Roles.Student;
            else user.SetLocked(true);
            await db.SaveChangesAsync();
        }

        Assert.Equal(token, client.DefaultRequestHeaders.Authorization);
        var response = await client.GetAsync(path);
        Assert.Equal(token, client.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class SubmissionFactory : WebApplicationFactory<Program>,
        IIntegrationEventHandler<StudentEnrolledIntegrationEvent>, IIntegrationEventHandler<InvoiceCreatedIntegrationEvent>
    {
        private readonly List<SqliteConnection> connections = [];
        public System.Collections.Concurrent.ConcurrentBag<StudentEnrolledIntegrationEvent> EnrollmentEvents { get; } = [];
        public System.Collections.Concurrent.ConcurrentBag<InvoiceCreatedIntegrationEvent> InvoiceEvents { get; } = [];
        public Task HandleAsync(StudentEnrolledIntegrationEvent value, CancellationToken ct = default)
        { EnrollmentEvents.Add(value); return Task.CompletedTask; }
        public Task HandleAsync(InvoiceCreatedIntegrationEvent value, CancellationToken ct = default)
        { InvoiceEvents.Add(value); return Task.CompletedTask; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Identity:InitializeDatabase"] = "false", ["Academic:InitializeDatabase"] = "false",
                ["Enrollment:InitializeDatabase"] = "false", ["MediaNotification:InitializeDatabase"] = "false",
                ["Finance:InitializeDatabase"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                Sqlite<IdentityDbContext>(services); Sqlite<AcademicDbContext>(services);
                Sqlite<EnrollmentDbContext>(services); Sqlite<MediaNotificationDbContext>(services);
                Sqlite<FinanceDbContext>(services);
                services.AddSingleton<IIntegrationEventHandler<StudentEnrolledIntegrationEvent>>(this);
                services.AddSingleton<IIntegrationEventHandler<InvoiceCreatedIntegrationEvent>>(this);
                services.RemoveAll<IObjectStorage>(); services.AddSingleton<IObjectStorage, DownloadStorage>();
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

    private sealed class DownloadStorage : IObjectStorage
    {
        public Task<Uri> PresignGetAsync(string key, string name, DateTimeOffset expiresAt, CancellationToken ct) => Task.FromResult(new Uri("https://storage.example/" + key));
        public Task<Uri> PresignPutAsync(string key, string type, DateTimeOffset expiresAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task CopyAsync(string source, string destination, string etag, string type, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
