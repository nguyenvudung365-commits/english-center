using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Contracts;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class IdentityProfileTests : IAsyncLifetime
{
    private const string Password = "Test-password-123!";
    private readonly ProfileFactory factory = new();
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>().Database.EnsureCreatedAsync();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Student, Roles.Teacher })
        {
            var user = User.Create($"{role}@example.test", role, role, passwords.Hash(Password), DateTimeOffset.UtcNow);
            if (role == Roles.Student) user.AssignStudentCode($"HV900001-{user.CreatedAt.UtcDateTime:yy}");
            db.Users.Add(user);
        }
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); }

    private async Task Login(string role)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest($"{role}@example.test", Password));
        response.EnsureSuccessStatusCode();
        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    private async Task<JsonElement> Profile()
    {
        var response = await client.GetAsync("/api/identity/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task Anonymous_CannotReadOrWriteProfile()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/identity/me", new { })).StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Staff")]
    [InlineData("Teacher")]
    public async Task ActiveRoles_CanReadTheirOwnProfile(string role)
    {
        await Login(role);
        var profile = await Profile();
        Assert.Equal(role, profile.GetProperty("role").GetString());
        Assert.Equal($"{role}@example.test", profile.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("studentCode").ValueKind);
        Assert.False(profile.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task ManagerCreatesStudent_WithImmutableCode_StudentUpdatesOnlyOwnProfile()
    {
        await Login("Staff");
        var created = await client.PostAsJsonAsync("/api/identity/users", new
        {
            email = "new@example.test", fullName = "New Student", password = Password, role = "Student", phone = "0901234567"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches("^HV[0-9]{6}-[0-9]{2}$", account.GetProperty("studentCode").GetString()!);
        Assert.Equal("Staff", (await Profile()).GetProperty("role").GetString());

        var login = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest("new@example.test", Password));
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var profile = await Profile();
        var updated = await client.PutAsJsonAsync("/api/identity/me", new
        {
            fullName = "  Updated Student  ", email = "updated@example.test", phone = "  0912345678  ",
            avatarFileId = (Guid?)null, expectedRevision = profile.GetProperty("revision").GetGuid(),
            role = "Admin", studentCode = "HV999999-99", userId = Guid.NewGuid(), isLocked = true
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await Profile();
        Assert.Equal("Updated Student", saved.GetProperty("fullName").GetString());
        Assert.Equal("updated@example.test", saved.GetProperty("email").GetString());
        Assert.Equal("0912345678", saved.GetProperty("phone").GetString());
        Assert.Equal("Student", saved.GetProperty("role").GetString());
        Assert.Equal(profile.GetProperty("studentCode").GetString(), saved.GetProperty("studentCode").GetString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal("Staff", (await db.Users.SingleAsync(x => x.Role == "Staff")).FullName);
    }

    [Fact]
    public async Task DuplicateEmailAndStaleRevision_Return409WithoutChangingProfile()
    {
        await Login("Teacher");
        var profile = await Profile();
        foreach (var item in new[]
        {
            (Email: "STUDENT@example.test", Revision: profile.GetProperty("revision").GetGuid()),
            (Email: "free@example.test", Revision: Guid.NewGuid())
        })
        {
            var response = await client.PutAsJsonAsync("/api/identity/me", new
            {
                fullName = "Changed", email = item.Email, phone = (string?)null,
                avatarFileId = (Guid?)null, expectedRevision = item.Revision
            });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        Assert.Equal("Teacher", (await Profile()).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task InvalidFieldsAndUnknownAvatar_Return400_AndLockedUserCannotRead()
    {
        await Login("Teacher");
        var profile = await Profile();
        foreach (var avatar in new Guid?[] { null, Guid.NewGuid() })
        {
            var response = await client.PutAsJsonAsync("/api/identity/me", new
            {
                fullName = avatar is null ? " " : "Valid Name", email = "Teacher@example.test",
                phone = (string?)null, avatarFileId = avatar, expectedRevision = profile.GetProperty("revision").GetGuid()
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Users.SingleAsync(x => x.Role == "Teacher")).SetLocked(true);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/identity/me")).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin, Roles.Student)]
    [InlineData(Roles.Admin, Roles.Teacher)]
    [InlineData(Roles.Staff, Roles.Student)]
    [InlineData(Roles.Staff, Roles.Teacher)]
    public async Task Managers_CreateBothAllowedRoles_WithoutReplacingTheirSession(string manager, string role)
    {
        await Login(manager);
        var before = await Profile();
        var authorization = client.DefaultRequestHeaders.Authorization;
        var response = await client.PostAsJsonAsync("/api/identity/users", new
        {
            email = "created@example.test", fullName = "Created User", password = Password, role, phone = "0912345678"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(role, created.GetProperty("role").GetString());
        Assert.Equal("0912345678", created.GetProperty("phone").GetString());
        foreach (var secret in new[] { "password", "passwordHash", "accessToken", "refreshToken" })
            Assert.False(created.TryGetProperty(secret, out _));
        Assert.Equal(authorization, client.DefaultRequestHeaders.Authorization);
        Assert.Equal(before.GetProperty("id").GetGuid(), (await Profile()).GetProperty("id").GetGuid());
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.SingleAsync(x => x.Email == "created@example.test");
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordService>().Verify(stored.PasswordHash, Password));
        if (role == Roles.Student)
            Assert.Matches($"^HV[0-9]{{6}}-{stored.CreatedAt.UtcDateTime:yy}$", stored.StudentCode!);
        else
        {
            Assert.Null(stored.StudentCode);
            Assert.Equal(stored.Id, (await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Teachers.SingleAsync()).Id);
        }
    }

    [Theory]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Teacher)]
    public async Task NonManagers_CannotCreateAccounts(string role)
    {
        await Login(role);
        var response = await client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest("created@example.test", "Created", Password, Roles.Student));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(4, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData(Roles.Admin, Roles.Admin)]
    [InlineData(Roles.Admin, Roles.Staff)]
    [InlineData(Roles.Staff, Roles.Admin)]
    [InlineData(Roles.Staff, Roles.Staff)]
    public async Task Managers_CannotCreatePrivilegedRoles(string manager, string role)
    {
        await Login(manager);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/identity/users",
            new CreateUserRequest("created@example.test", "Created", Password, role))).StatusCode);
    }

    [Theory]
    [InlineData("locked", HttpStatusCode.Forbidden)]
    [InlineData("deleted", HttpStatusCode.Unauthorized)]
    [InlineData("changed-role", HttpStatusCode.Forbidden)]
    public async Task ManagerToken_RechecksPersistedAccount_OnProfileAndCreation(string kind, HttpStatusCode expected)
    {
        await Login(Roles.Admin);
        var profile = await Profile();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = await db.Users.SingleAsync(x => x.Role == Roles.Admin);
            if (kind == "locked") user.SetLocked(true);
            else if (kind == "deleted") db.Users.Remove(user);
            else db.Entry(user).Property(x => x.Role).CurrentValue = Roles.Staff;
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, (await client.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(expected, (await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest(
            "Changed", "Admin@example.test", null, null, profile.GetProperty("revision").GetGuid()))).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest(
            "created@example.test", "Created", Password, Roles.Student))).StatusCode);
    }

    public static IEnumerable<object[]> InvalidProfileFields()
    {
        yield return ["FullName", " "];
        yield return ["FullName", new string('n', 201)];
        yield return ["Email", " "];
        yield return ["Email", "not-an-email"];
        yield return ["Email", new string('e', 245) + "@example.test"];
        yield return ["Phone", new string('1', 33)];
    }

    [Theory]
    [MemberData(nameof(InvalidProfileFields))]
    public async Task Profile_InvalidField_ReturnsFieldErrorWithoutMutation(string field, string value)
    {
        await Login(Roles.Student);
        var before = await Profile();
        var request = new UpdateProfileRequest("Student", "Student@example.test", null, null, before.GetProperty("revision").GetGuid());
        request = field switch { "FullName" => request with { FullName = value }, "Email" => request with { Email = value }, _ => request with { Phone = value } };
        await AssertFieldError(await client.PutAsJsonAsync("/api/identity/me", request), field);
        Assert.Equal(before.GetRawText(), (await Profile()).GetRawText());
    }

    [Theory]
    [MemberData(nameof(InvalidProfileFields))]
    public async Task Creation_InvalidField_ReturnsFieldErrorWithoutCreating(string field, string value)
    {
        await Login(Roles.Staff);
        var request = new CreateUserRequest("created@example.test", "Created", Password, Roles.Student);
        request = field switch { "FullName" => request with { FullName = value }, "Email" => request with { Email = value }, _ => request with { Phone = value } };
        await AssertFieldError(await client.PostAsJsonAsync("/api/identity/users", request), field);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(4, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Profile_ExactMaximumLengthsAndBlankPhone_AreAccepted()
    {
        await Login(Roles.Student);
        var before = await Profile();
        var email = new string('e', 64) + "@" + new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 61);
        Assert.Equal(254, email.Length);
        var request = new UpdateProfileRequest(new string('n', 200), email, new string('1', 32), null, before.GetProperty("revision").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/identity/me", request)).StatusCode);
        var saved = await Profile();
        Assert.Equal(request.FullName, saved.GetProperty("fullName").GetString());
        Assert.Equal(email, saved.GetProperty("email").GetString());
        Assert.Equal(request.Phone, saved.GetProperty("phone").GetString());
        Assert.Equal(before.GetProperty("studentCode").GetString(), saved.GetProperty("studentCode").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/identity/me", request with
        { Phone = "   ", ExpectedRevision = saved.GetProperty("revision").GetGuid() })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Profile()).GetProperty("phone").ValueKind);
    }

    [Fact]
    public async Task StudentCreation_CodesAreUniqueAndStableAcrossProfileChanges()
    {
        await Login(Roles.Admin);
        var codes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest($"student{i}@example.test", "Student", Password, Roles.Student));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            codes.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("studentCode").GetString()!);
        }
        Assert.Equal(3, codes.Distinct().Count());
        using var scope = factory.Services.CreateScope();
        foreach (var student in await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(x => x.Role == Roles.Student).ToListAsync())
            Assert.EndsWith($"-{student.CreatedAt.UtcDateTime:yy}", student.StudentCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Teacher)]
    public async Task AllRoles_CanUpdateOnlyTheirOwnProfile(string role)
    {
        await Login(role);
        var before = await Profile();
        var response = await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest(
            "Updated " + role, $"{role}@example.test", "  0123456789  ", null, before.GetProperty("revision").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Profile();
        Assert.Equal(before.GetProperty("id").GetGuid(), saved.GetProperty("id").GetGuid());
        Assert.Equal("Updated " + role, saved.GetProperty("fullName").GetString());
        Assert.Equal("0123456789", saved.GetProperty("phone").GetString());
        Assert.NotEqual(before.GetProperty("revision").GetGuid(), saved.GetProperty("revision").GetGuid());
        using var scope = factory.Services.CreateScope();
        var others = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(x => x.Role != role).ToListAsync();
        Assert.All(others, x => Assert.Equal(x.Role, x.FullName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Profile_MissingOrEmptyRevision_IsRejectedBeforeMutation(string? revision)
    {
        await Login(Roles.Student);
        var before = await Profile();
        await AssertFieldError(await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest(
            "Changed", "Student@example.test", null, null, revision is null ? null : Guid.Parse(revision))), "ExpectedRevision");
        Assert.Equal(before.GetRawText(), (await Profile()).GetRawText());
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("foreign")]
    [InlineData("submission")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task Avatar_InvalidReference_IsGenericAndPreservesExistingProfile(string kind)
    {
        await Login(Roles.Student);
        var before = await Profile();
        var fileId = kind == "missing" ? Guid.NewGuid() : kind == "empty" ? Guid.Empty : await AvatarFile(
            kind == "foreign" ? Guid.NewGuid() : before.GetProperty("id").GetGuid(), kind != "pending", kind == "submission" ? "submission" : "avatar");
        var response = await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest("Changed", "Student@example.test", null,
            fileId, before.GetProperty("revision").GetGuid()));
        await AssertFieldError(response, "AvatarFileId");
        var error = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fileId.ToString(), error);
        Assert.Equal(before.GetRawText(), (await Profile()).GetRawText());
    }

    [Fact]
    public async Task Avatar_OwnConfirmedImage_CanBeReadUpdatedAndRemoved_ButNotUsedAsSubmission()
    {
        await Login(Roles.Student);
        var before = await Profile();
        var fileId = await AvatarFile(before.GetProperty("id").GetGuid());
        var request = new UpdateProfileRequest("Student", "Student@example.test", null, fileId, before.GetProperty("revision").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/identity/me", request)).StatusCode);
        var saved = await Profile();
        Assert.Equal(fileId, saved.GetProperty("avatarFileId").GetGuid());
        Assert.StartsWith("https://storage.invalid/", saved.GetProperty("avatarUrl").GetString());
        Assert.True(saved.GetProperty("avatarUrlExpiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISubmissionFileAccess>().GetConfirmedAsync(before.GetProperty("id").GetGuid(), [fileId], default));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/identity/me", request with
        { AvatarFileId = null, ExpectedRevision = saved.GetProperty("revision").GetGuid() })).StatusCode);
        var removed = await Profile();
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("avatarFileId").ValueKind);
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("avatarUrl").ValueKind);
        Assert.True(await scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>().StoredFiles.AnyAsync(x => x.Id == fileId));
    }

    [Fact]
    public async Task TeacherProfileVersion_ReplayAndOlderSnapshotsCannotRegressDirectory()
    {
        await Login(Roles.Teacher);
        var before = await Profile();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest(
            "Updated Teacher", "updated-teacher@example.test", null, null, before.GetProperty("revision").GetGuid()))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await identity.Users.SingleAsync(x => x.Role == Roles.Teacher);
        Assert.Equal(1L, user.ProfileVersion);
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        await bus.PublishAsync(new UserRegisteredIntegrationEvent(user.Id, "old@example.test", "Old Teacher", Roles.Teacher, 0));
        await bus.PublishAsync(new UserProfileUpdatedIntegrationEvent(user.Id, "old@example.test", "Old Teacher", Roles.Teacher, 0));
        await scope.ServiceProvider.GetRequiredService<UserSyncService>().RepublishAsync(new(Roles.Teacher), default);
        var teacher = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Teachers.AsNoTracking().SingleAsync();
        Assert.Equal(user.ProfileVersion, teacher.ProfileVersion);
        Assert.Equal(user.FullName, teacher.FullName);
        Assert.Equal(user.Email, teacher.Email);
    }

    [Fact]
    public async Task TeacherSyncFailure_ReturnsExplicit503_WithCommittedProfile_AndRepublishRepairsDirectory()
    {
        await Login(Roles.Teacher);
        var before = await Profile();
        var userId = before.GetProperty("id").GetGuid();
        Assert.IsType<InMemoryEventBus>(factory.Services.GetRequiredService<IEventBus>());
        await factory.Services.GetRequiredService<IEventBus>().PublishAsync(new UserRegisteredIntegrationEvent(
            userId, "Teacher@example.test", "Teacher", Roles.Teacher, 0));
        factory.TeacherSync.Fail = true;

        var response = await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest(
            "Saved Teacher", "saved-teacher@example.test", null, null, before.GetProperty("revision").GetGuid()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(503, problem.GetProperty("status").GetInt32());
        Assert.Equal("Profile was saved, but teacher synchronization failed. Reload your profile and ask an administrator to republish.",
            problem.GetProperty("title").GetString());
        Assert.DoesNotContain("private Academic failure", await response.Content.ReadAsStringAsync());
        var authoritative = await Profile();
        Assert.Equal(userId, authoritative.GetProperty("id").GetGuid());
        Assert.Equal("Saved Teacher", authoritative.GetProperty("fullName").GetString());
        Assert.Equal("saved-teacher@example.test", authoritative.GetProperty("email").GetString());
        Assert.NotEqual(before.GetProperty("revision").GetGuid(), authoritative.GetProperty("revision").GetGuid());
        var observed = Assert.Single(factory.TeacherSync.Observed);
        Assert.Equal(1L, observed.ProfileVersion);
        using (var verification = factory.Services.CreateScope())
        {
            var teacher = await verification.ServiceProvider.GetRequiredService<AcademicDbContext>().Teachers.AsNoTracking().SingleAsync();
            Assert.Equal("Teacher", teacher.FullName);
            Assert.Equal("Teacher@example.test", teacher.Email);
            Assert.Equal(0L, teacher.ProfileVersion);
            var stored = await verification.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AsNoTracking().SingleAsync(x => x.Id == userId);
            Assert.Equal(1L, stored.ProfileVersion);
        }

        factory.TeacherSync.Fail = false;
        using var repair = factory.Services.CreateScope();
        await repair.ServiceProvider.GetRequiredService<UserSyncService>().RepublishAsync(new(Roles.Teacher), default);
        var repaired = await repair.ServiceProvider.GetRequiredService<AcademicDbContext>().Teachers.AsNoTracking().SingleAsync();
        Assert.Equal("Saved Teacher", repaired.FullName);
        Assert.Equal("saved-teacher@example.test", repaired.Email);
        Assert.Equal(1L, repaired.ProfileVersion);
    }

    [Fact]
    public async Task TeacherPhoneOnlyEdit_DoesNotRequireProfileSnapshotPublication()
    {
        await Login(Roles.Teacher);
        var before = await Profile();
        factory.TeacherSync.Fail = true;
        var response = await client.PutAsJsonAsync("/api/identity/me", new UpdateProfileRequest(
            "Teacher", "Teacher@example.test", "0123456789", null, before.GetProperty("revision").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.TeacherSync.Observed);
        Assert.Equal("0123456789", (await Profile()).GetProperty("phone").GetString());
    }

    private async Task<Guid> AvatarFile(Guid owner, bool confirmed = true, string purpose = "avatar")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>();
        var file = StoredFile.Create(owner, "avatar.png", "image/png", 100, purpose, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        if (confirmed) file.Complete($"files/{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private static async Task AssertFieldError(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), x => string.Equals(x.Name, field, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ProfileFactory : WebApplicationFactory<Program>
    {
        public TeacherSyncFault TeacherSync { get; } = new();
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly SqliteConnection academic = new("Data Source=:memory:");
        private readonly SqliteConnection media = new("Data Source=:memory:");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            connection.Open();
            academic.Open();
            media.Open();
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Identity:InitializeDatabase"] = "false",
                ["Academic:InitializeDatabase"] = "false",
                ["Enrollment:InitializeDatabase"] = "false",
                ["MediaNotification:InitializeDatabase"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(connection));
                services.RemoveAll<DbContextOptions<AcademicDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AcademicDbContext>>();
                services.AddDbContext<AcademicDbContext>(options => options.UseSqlite(academic));
                services.RemoveAll<DbContextOptions<MediaNotificationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<MediaNotificationDbContext>>();
                services.AddDbContext<MediaNotificationDbContext>(options => options.UseSqlite(media));
                services.RemoveAll<IStudentCodeGenerator>();
                services.AddSingleton<IStudentCodeGenerator, TestStudentCodes>();
                services.RemoveAll<IObjectStorage>();
                services.AddSingleton<IObjectStorage, ProfileStorage>();
                services.AddScoped<UserRegisteredIntegrationEventHandler>();
                services.RemoveAll<IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>>();
                services.AddScoped<IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>>(sp =>
                    new FaultingTeacherProfileHandler(sp.GetRequiredService<UserRegisteredIntegrationEventHandler>(), TeacherSync));
                services.AddSingleton<IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>>(TeacherSync);
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
            await academic.DisposeAsync();
            await media.DisposeAsync();
        }
    }

    // SQLite fixture allocation only; this is not evidence for the PostgreSQL sequence.
    private sealed class TestStudentCodes : IStudentCodeGenerator
    {
        private int sequence;
        public Task<string> AllocateAsync(DateTimeOffset createdAt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult($"HV{Interlocked.Increment(ref sequence):D6}-{createdAt.UtcDateTime:yy}");
        }
    }

    private sealed class ProfileStorage : IObjectStorage
    {
        public Task<Uri> PresignGetAsync(string key, string fileName, DateTimeOffset expiresAt, CancellationToken ct) =>
            Task.FromResult(new Uri($"https://storage.invalid/{key}"));
        public Task<Uri> PresignPutAsync(string key, string contentType, DateTimeOffset expiresAt, CancellationToken ct) => throw new NotSupportedException();
        public Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
        public Task CopyAsync(string sourceKey, string destinationKey, string sourceETag, string contentType, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TeacherSyncFault : IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
    {
        public bool Fail { get; set; }
        public List<UserProfileUpdatedIntegrationEvent> Observed { get; } = [];
        public Task HandleAsync(UserProfileUpdatedIntegrationEvent integrationEvent, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Observed.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingTeacherProfileHandler(UserRegisteredIntegrationEventHandler academic, TeacherSyncFault fault)
        : IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
    {
        public Task HandleAsync(UserProfileUpdatedIntegrationEvent integrationEvent, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return fault.Fail ? throw new InvalidOperationException("private Academic failure") : academic.HandleAsync(integrationEvent, ct);
        }
    }
}
