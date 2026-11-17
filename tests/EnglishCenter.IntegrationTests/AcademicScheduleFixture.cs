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
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Synthetic accounts and registrations only; each test owns its host and databases.
internal sealed class AcademicScheduleFixture(string? postgres = null, IInterceptor? interceptor = null)
    : WebApplicationFactory<Program>
{
    internal const string ClassesUrl = "/api/academic/classes";
    internal const string SessionsUrl = "/api/academic/sessions";
    private const string Password = "Schedule-test-123!";
    private readonly List<SqliteConnection> connections = [];
    internal Dictionary<string, Guid> Users { get; } = [];
    internal ScheduleClock Clock { get; } = new();
    internal Guid First { get; private set; }
    internal Guid Second { get; private set; }
    internal Guid Other { get; private set; }
    internal HttpClient Client { get; private set; } = null!;

    internal async Task InitializeAsync(string? academicMigration = null)
    {
        Client = CreateClient();
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var academic = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var enrollment = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        if (postgres is null)
        {
            await identity.Database.EnsureCreatedAsync();
            await academic.Database.EnsureCreatedAsync();
            await enrollment.Database.EnsureCreatedAsync();
        }
        else
        {
            await identity.Database.MigrateAsync();
            await enrollment.Database.MigrateAsync();
            await academic.GetService<IMigrator>().MigrateAsync(academicMigration);
        }

        var hash = scope.ServiceProvider.GetRequiredService<IPasswordService>().Hash(Password);
        var now = DateTimeOffset.UtcNow;
        var studentNumber = 1;
        foreach (var (name, role) in new[] { ("admin", Roles.Admin), ("staff", Roles.Staff),
                     ("teacher", Roles.Teacher), ("other-teacher", Roles.Teacher),
                     ("student", Roles.Student), ("other-student", Roles.Student) })
        {
            var user = User.Create($"{name}@example.test", name, role, hash, now);
            if (role == Roles.Student) user.AssignStudentCode($"HV{studentNumber++:D6}-{now:yy}");
            identity.Users.Add(user);
            Users[name] = user.Id;
            if (role == Roles.Teacher) academic.Teachers.Add(Teacher.Create(user.Id, name, user.Email, 7));
            if (role == Roles.Student) enrollment.Students.Add(new Student(user.Id, name, user.Email));
        }
        await identity.SaveChangesAsync();
        var course = Course.Create(Guid.NewGuid(), "SCHEDULE", "Synthetic schedule course");
        academic.Courses.Add(course);
        var classes = new[] { NewClass("A-01", "Lớp Việt 🗓", Users["teacher"]),
            NewClass("A-02", "Second class", Users["teacher"]), NewClass("B-01", "Other class", Users["other-teacher"]) };
        First = classes[0].Id; Second = classes[1].Id; Other = classes[2].Id;
        academic.Classes.AddRange(classes);
        foreach (var item in classes)
            enrollment.Classes.Add(new ClassSnapshot(item.Id, item.Code, item.StartDate, item.EndDate, item.MaxStudents, item.TuitionFee));

        // Deliberately stale projection: active membership has no event, revoked/cancelled ones still do.
        var revoked = new Registration(Users["student"], First, 0, now.UtcDateTime, 3, 5, 2);
        enrollment.Enrollments.Add(revoked);
        enrollment.Entry(revoked).Property(x => x.IsActive).CurrentValue = false;
        academic.ClassStudents.Add(new ClassStudent(revoked.Id, Users["student"], First));
        var cancelled = new Registration(Users["student"], Other, 0, now.UtcDateTime, 3, 5, 2);
        enrollment.Enrollments.Add(cancelled);
        enrollment.Entry(cancelled).Property(x => x.Status).CurrentValue = "Cancelled";
        academic.ClassStudents.Add(new ClassStudent(cancelled.Id, Users["student"], Other));
        enrollment.Enrollments.Add(new Registration(Users["student"], Second, 0, now.UtcDateTime, 3, 5, 2));
        enrollment.Enrollments.Add(new Registration(Users["other-student"], Other, 0, now.UtcDateTime, 3, 5, 2));
        await academic.SaveChangesAsync();
        await enrollment.SaveChangesAsync();

        Class NewClass(string code, string name, Guid teacher) => Class.Create(code, name, course.Id,
            ClassType.Main, new(2026, 12, 31), new(2027, 1, 2), 20, 0, teacher, now);
    }

    internal async Task AsAsync(string name, HttpClient? client = null)
    {
        client ??= Client;
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.PostAsJsonAsync("/api/identity/auth/login", new { email = $"{name}@example.test", password = Password });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    internal async Task ChangeAccountAsync(string actor, string change)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == Users[actor]);
        if (change == "delete") db.Users.Remove(user);
        else if (change == "role") db.Entry(user).Property(x => x.Role).CurrentValue = user.Role == Roles.Student ? Roles.Teacher : Roles.Student;
        else user.SetLocked(true);
        await db.SaveChangesAsync();
    }

    internal async Task RevokeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        var registration = await db.Enrollments.SingleAsync(x => x.StudentId == Users["student"] && x.ClassId == Second);
        db.Entry(registration).Property(x => x.IsActive).CurrentValue = false;
        await db.SaveChangesAsync();
    }

    internal Dictionary<string, object?> Body(Guid? classId = null,
        string start = "2027-01-01T10:00:00Z", string end = "2027-01-01T11:00:00Z") => new()
    {
        ["classId"] = classId ?? First, ["startsAtUtc"] = start, ["endsAtUtc"] = end,
        ["unit"] = " Unit 1 ", ["lesson"] = " Bài học ' Việt 🗓 ",
        ["skills"] = new[] { "Speaking", "Listening" }, ["sessionType"] = "Regular", ["examSlot"] = " "
    };

    internal static string Range(string from = "2026-12-30T17:00:00Z", string to = "2027-01-02T17:00:00Z", Guid? classId = null) =>
        $"{SessionsUrl}?fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}" + (classId is null ? "" : $"&classId={classId}");

    internal static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Identity:InitializeDatabase"] = "false", ["Academic:InitializeDatabase"] = "false",
            ["Enrollment:InitializeDatabase"] = "false", ["Finance:InitializeDatabase"] = "false",
            ["MediaNotification:InitializeDatabase"] = "false"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            Replace<IdentityDbContext>(services, "identity"); Replace<AcademicDbContext>(services, "academic");
            Replace<EnrollmentDbContext>(services, "enrollment"); Replace<FinanceDbContext>(services, "finance");
            Replace<MediaNotificationDbContext>(services, "media_notification");
        });
    }

    private void Replace<T>(IServiceCollection services, string schema) where T : DbContext
    {
        services.RemoveAll<DbContextOptions<T>>(); services.RemoveAll<IDbContextOptionsConfiguration<T>>();
        SqliteConnection? connection = null;
        if (postgres is null)
        {
            connection = new SqliteConnection("Data Source=:memory:"); connection.Open(); connections.Add(connection);
        }
        services.AddDbContext<T>(options =>
        {
            if (postgres is null) options.UseSqlite(connection!);
            else options.UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));
            if (typeof(T) == typeof(AcademicDbContext) && interceptor is not null) options.AddInterceptors(interceptor);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await base.DisposeAsync();
        foreach (var connection in connections) await connection.DisposeAsync();
    }

    internal sealed class ScheduleClock : TimeProvider
    {
        internal DateTimeOffset? UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow ?? TimeProvider.System.GetUtcNow();
    }
}
