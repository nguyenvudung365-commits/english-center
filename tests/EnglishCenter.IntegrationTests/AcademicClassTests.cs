using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class AcademicClassTests : IAsyncLifetime
{
    private const string Password = "Test-password-123!";
    private const string ClassesUrl = "/api/academic/classes";
    private readonly AcademicFactory _factory = new();
    private HttpClient _client = null!;
    private Guid _courseId;
    private Guid _teacherId;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await identity.Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Database.EnsureCreatedAsync();
            var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
            foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Student, Roles.Teacher })
                identity.Users.Add(User.Create($"{role}@example.test", role, role, passwords.Hash(Password), DateTimeOffset.UtcNow));
            await identity.SaveChangesAsync();
        }

        // Giáo viên đi qua luồng thật: Identity tạo tài khoản -> UserRegistered -> academic.teachers.
        Authorize(await Login(Roles.Staff));
        var created = await _client.PostAsJsonAsync("/api/identity/users",
            new CreateUserRequest("giao.vien@example.test", "Nguyen Van Giao", Password, Roles.Teacher));
        created.EnsureSuccessStatusCode();
        _teacherId = (await created.Content.ReadFromJsonAsync<UserResponse>())!.Id;
        var courses = (await _client.GetFromJsonAsync<List<CourseResponse>>("/api/academic/courses"))!;
        _courseId = courses.Single(x => x.Code == "IELTS-FOUNDATION").Id;
        _client.DefaultRequestHeaders.Authorization = null;
    }

    public async Task DisposeAsync() { _client.Dispose(); await _factory.DisposeAsync(); }

    [Theory]
    [InlineData(Roles.Staff, HttpStatusCode.Created)]
    [InlineData(Roles.Admin, HttpStatusCode.Created)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Student, HttpStatusCode.Forbidden)]
    public async Task CreateClass_RequiresStaffOrAdmin(string role, HttpStatusCode expected)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync(ClassesUrl, Valid())).StatusCode);
        Authorize(await Login(role));
        Assert.Equal(expected, (await _client.PostAsJsonAsync(ClassesUrl, Valid())).StatusCode);
        if (expected != HttpStatusCode.Created) Assert.Empty(_factory.ClassCreated.Events);
    }

    [Fact]
    public async Task CreateClass_Returns201_PublishesClassCreated_AndCanBeReadBack()
    {
        Authorize(await Login(Roles.Staff));
        var response = await _client.PostAsJsonAsync(ClassesUrl, Valid(" ie-2611-a "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ClassResponse>())!;
        Assert.Equal("IE-2611-A", created.Code);
        Assert.Equal("Main", created.ClassType);
        Assert.Equal("IELTS-FOUNDATION", created.CourseCode);
        Assert.Equal("Nguyen Van Giao", created.TeacherName);
        Assert.EndsWith($"{ClassesUrl}/{created.Id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var published = Assert.Single(_factory.ClassCreated.Events);
        Assert.Equal(created.Id, published.ClassId);
        Assert.Equal("IE-2611-A", published.ClassCode);
        Assert.Equal(new DateOnly(2026, 11, 2), published.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 30), published.EndDate);
        Assert.Equal(20, published.MaxStudents);
        Assert.Equal(4_500_000m, published.TuitionFee);
        Assert.Equal("Main", published.ClassType);

        var fetched = (await _client.GetFromJsonAsync<ClassResponse>(response.Headers.Location))!;
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Code, fetched.Code);
        Assert.Equal(created.TeacherId, fetched.TeacherId);
        Assert.Equal(created.TuitionFee, fetched.TuitionFee);
        Assert.Equal(created.Status, fetched.Status);
    }

    [Fact]
    public async Task CreateClass_DuplicateCodeIgnoringCase_Returns409()
    {
        Authorize(await Login(Roles.Staff));
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync(ClassesUrl, Valid("IE-DUP"))).StatusCode);
        var duplicate = await _client.PostAsJsonAsync(ClassesUrl, Valid("ie-dup"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single(_factory.ClassCreated.Events);
    }

    [Theory]
    [InlineData("endDate", "2026-11-02", "EndDate")]
    [InlineData("endDate", "2026-10-01", "EndDate")]
    [InlineData("maxStudents", "0", "MaxStudents")]
    [InlineData("tuitionFee", "-1", "TuitionFee")]
    [InlineData("tuitionFee", "4500000.5", "TuitionFee")]
    [InlineData("classType", "Online", "ClassType")]
    [InlineData("code", "IE 01", "Code")]
    public async Task CreateClass_InvalidValue_Returns400WithFieldError(string property, string value, string errorKey)
    {
        Authorize(await Login(Roles.Staff));
        var body = ValidJson();
        body[property] = JsonSerializer.SerializeToElement(property is "maxStudents" or "tuitionFee"
            ? decimal.Parse(value, CultureInfo.InvariantCulture) : (object)value);

        var response = await _client.PostAsJsonAsync(ClassesUrl, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(errorKey, out _), problem.RootElement.ToString());
        Assert.Empty(_factory.ClassCreated.Events);
    }

    [Theory]
    [InlineData("courseId")]
    [InlineData("maxStudents")]
    [InlineData("startDate")]
    public async Task CreateClass_MissingField_Returns400(string property)
    {
        Authorize(await Login(Roles.Staff));
        var body = ValidJson();
        body.Remove(property);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(ClassesUrl, body)).StatusCode);
    }

    [Fact]
    public async Task CreateClass_TeacherNotSynchronizedToAcademic_Returns400()
    {
        // Tài khoản Teacher seed thẳng vào identity nên không phát UserRegistered -> không có trong academic.teachers.
        Guid unsynchronized;
        using (var scope = _factory.Services.CreateScope())
            unsynchronized = (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
                .SingleAsync(x => x.Email == "Teacher@example.test")).Id;
        Authorize(await Login(Roles.Staff));

        var response = await _client.PostAsJsonAsync(ClassesUrl, Valid() with { TeacherId = unsynchronized });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("TeacherId", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetClass_Unknown_Returns404()
    {
        Authorize(await Login(Roles.Staff));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{ClassesUrl}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Lookups_ReturnSeededCourses_AndSynchronizedTeachers_ForManagersOnly()
    {
        Authorize(await Login(Roles.Student));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/academic/teachers")).StatusCode);

        Authorize(await Login(Roles.Admin));
        var courses = (await _client.GetFromJsonAsync<List<CourseResponse>>("/api/academic/courses"))!;
        Assert.Equal(new[] { "IELTS-ADVANCED", "IELTS-FOUNDATION", "IELTS-INTERMEDIATE" }, courses.Select(x => x.Code));
        var teacher = Assert.Single((await _client.GetFromJsonAsync<List<TeacherResponse>>("/api/academic/teachers"))!);
        Assert.Equal(_teacherId, teacher.Id);
    }

    private CreateClassRequest Valid(string code = "IE-2611-A") => new(code, "IELTS Foundation K1", _courseId, "Main",
        new DateOnly(2026, 11, 2), new DateOnly(2027, 1, 30), 20, 4_500_000m, _teacherId);

    private Dictionary<string, JsonElement> ValidJson() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(Valid(), JsonSerializerOptions.Web))!;

    private void Authorize(TokenResponse token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

    private async Task<TokenResponse> Login(string role)
    {
        var response = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest($"{role}@example.test", Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private sealed class AcademicFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _identity = new("Data Source=:memory:");
        private readonly SqliteConnection _academic = new("Data Source=:memory:");
        public EventRecorder<ClassCreatedIntegrationEvent> ClassCreated { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _identity.Open();
            _academic.Open();
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Identity:InitializeDatabase"] = "false",
                ["Academic:InitializeDatabase"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(o => o.UseSqlite(_identity));
                services.RemoveAll<DbContextOptions<AcademicDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AcademicDbContext>>();
                services.AddDbContext<AcademicDbContext>(o => o.UseSqlite(_academic));
                services.AddSingleton<IIntegrationEventHandler<ClassCreatedIntegrationEvent>>(ClassCreated);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _identity.DisposeAsync();
            await _academic.DisposeAsync();
        }
    }

    private sealed class EventRecorder<TEvent> : IIntegrationEventHandler<TEvent> where TEvent : IIntegrationEvent
    {
        public List<TEvent> Events { get; } = [];
        public Task HandleAsync(TEvent integrationEvent, CancellationToken ct = default)
        { Events.Add(integrationEvent); return Task.CompletedTask; }
    }
}

public sealed class AcademicConfigurationTests
{
    [Fact]
    public void PostgreSqlMigration_OnlyTouchesAcademicSchema_AndMatchesModel()
    {
        using var db = new AcademicDesignTimeFactory().CreateDbContext([]);
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE academic.courses", script);
        Assert.Contains("CREATE TABLE academic.teachers", script);
        Assert.Contains("CREATE TABLE academic.classes", script);
        Assert.Contains("academic.\"__EFMigrationsHistory\"", script);
        Assert.Contains("CHECK (end_date > start_date)", script);
        Assert.Contains("CHECK (max_students > 0)", script);
        Assert.Contains("CHECK (tuition_fee >= 0)", script);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_classes_code\" ON academic.classes (code)", script);
        Assert.Contains("tuition_fee numeric(12,0) NOT NULL", script);
        Assert.DoesNotContain("identity.", script);
        Assert.DoesNotContain("enrollment.", script);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
