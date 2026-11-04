using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// TV3: host và dữ liệu riêng, không gọi test/fixture của TV1 hoặc TV2.
public sealed class Tv3Week2Tests : IAsyncLifetime
{
    private const string Password = "Tv3-test-password-123!";
    private const string Url = "/api/academic/classes";
    private readonly ReviewFactory _factory = new();
    private HttpClient _client = null!;
    private Guid _courseId;
    private Guid _teacherId;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var academic = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        await identity.Database.EnsureCreatedAsync();
        await academic.Database.EnsureCreatedAsync();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Student, Roles.Teacher })
            identity.Users.Add(User.Create($"{role}@tv3.test", role, role, passwords.Hash(Password), DateTimeOffset.UtcNow));
        await identity.SaveChangesAsync();
        _courseId = await academic.Courses.Select(x => x.Id).FirstAsync();

        // Tạo giáo viên qua API để kiểm tra luôn UserRegistered -> read model Academic.
        await Login(Roles.Staff);
        var response = await _client.PostAsJsonAsync("/api/identity/users",
            new CreateUserRequest("new-teacher@tv3.test", "TV3 Teacher", Password, Roles.Teacher));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        _teacherId = (await response.Content.ReadFromJsonAsync<UserResponse>())!.Id;
        Assert.True(await academic.Teachers.AnyAsync(x => x.Id == _teacherId));
        _client.DefaultRequestHeaders.Authorization = null;
    }

    public async Task DisposeAsync() { _client.Dispose(); await _factory.DisposeAsync(); }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Student, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Teacher, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Staff, HttpStatusCode.Created)]
    [InlineData(Roles.Admin, HttpStatusCode.Created)]
    public async Task CreateAndRead_EnforceRole_WithoutUnauthorizedSideEffects(string? role, HttpStatusCode expected)
    {
        if (role is not null) await Login(role);
        var response = await _client.PostAsJsonAsync(Url, Valid());
        Assert.Equal(expected, response.StatusCode);
        await AssertState(expected == HttpStatusCode.Created ? 1 : 0);
        var readExpected = expected == HttpStatusCode.Created ? HttpStatusCode.OK : expected;
        foreach (var lookup in new[] { "/api/academic/courses", "/api/academic/teachers" })
            Assert.Equal(readExpected, (await _client.GetAsync(lookup)).StatusCode);
        if (expected != HttpStatusCode.Created)
            Assert.Equal(expected, (await _client.GetAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrExpiredJwt_CannotPersistOrPublish(bool expired)
    {
        var bearer = "invalid.token.signature";
        if (expired)
        {
            using var scope = _factory.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.SingleAsync(x => x.Role == Roles.Staff);
            bearer = scope.ServiceProvider.GetRequiredService<ITokenService>().Issue(user, DateTimeOffset.UtcNow.AddHours(-2)).AccessToken;
        }
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync(Url, Valid())).StatusCode);
        await AssertState(0);
    }

    [Theory]
    [InlineData("missing-course")]
    [InlineData("missing-teacher")]
    [InlineData("same-day")]
    [InlineData("negative-capacity")]
    [InlineData("fractional-fee")]
    [InlineData("overflow-fee")]
    [InlineData("numeric-type")]
    public async Task RejectedClass_DoesNotPersistOrPublish(string scenario)
    {
        await Login(Roles.Staff);
        var request = scenario switch
        {
            "missing-course" => Valid() with { CourseId = Guid.NewGuid() },
            "missing-teacher" => Valid() with { TeacherId = Guid.NewGuid() },
            "same-day" => Valid() with { EndDate = Valid().StartDate },
            "negative-capacity" => Valid() with { MaxStudents = -1 },
            "fractional-fee" => Valid() with { TuitionFee = 0.5m },
            "overflow-fee" => Valid() with { TuitionFee = 1_000_000_000_000m },
            _ => Valid() with { ClassType = "1" }
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(Url, request)).StatusCode);
        await AssertState(0);
    }

    [Theory]
    [InlineData("Main", 0)]
    [InlineData("Supplementary", 999_999_999_999)]
    public async Task ValidClass_EventMatchesCommittedData_AndDuplicateDoesNotRepublish(string type, long fee)
    {
        await Login(Roles.Staff);
        var before = DateTime.UtcNow;
        var response = await _client.PostAsJsonAsync(Url, Valid() with { Code = " tv3-a ", ClassType = type, TuitionFee = fee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ClassResponse>())!;
        var emitted = Assert.Single(_factory.Created);
        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Classes.SingleAsync();
        Assert.Equal(saved.Id, emitted.ClassId);
        Assert.Equal(saved.Code, emitted.ClassCode);
        Assert.Equal("TV3-A", emitted.ClassCode);
        Assert.Equal(saved.StartDate, emitted.StartDate);
        Assert.Equal(saved.EndDate, emitted.EndDate);
        Assert.Equal(saved.MaxStudents, emitted.MaxStudents);
        Assert.Equal(saved.TuitionFee, emitted.TuitionFee);
        Assert.Equal(type, emitted.ClassType);
        Assert.NotEqual(Guid.Empty, emitted.EventId);
        Assert.Equal(DateTimeKind.Utc, emitted.OccurredOnUtc.Kind);
        Assert.InRange(emitted.OccurredOnUtc, before, DateTime.UtcNow);
        var read = await _client.GetFromJsonAsync<ClassResponse>(response.Headers.Location);
        Assert.Equal(created, read);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync(Url, Valid() with { Code = "Tv3-A" })).StatusCode);
        await AssertState(1);
    }

    private CreateClassRequest Valid() => new("TV3-A", "TV3 review", _courseId, "Main",
        new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 1), 20, 4_500_000m, _teacherId);

    private async Task Login(string role)
    {
        var response = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest($"{role}@tv3.test", Password));
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private async Task AssertState(int count)
    {
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(count, await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Classes.CountAsync());
        Assert.Equal(count, _factory.Created.Count);
    }

    private sealed class ReviewFactory : WebApplicationFactory<Program>, IIntegrationEventHandler<ClassCreatedIntegrationEvent>
    {
        private readonly SqliteConnection _identity = new("Data Source=:memory:");
        private readonly SqliteConnection _academic = new("Data Source=:memory:");
        public List<ClassCreatedIntegrationEvent> Created { get; } = [];
        public Task HandleAsync(ClassCreatedIntegrationEvent value, CancellationToken ct = default)
        { Created.Add(value); return Task.CompletedTask; }

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
                services.AddSingleton<IIntegrationEventHandler<ClassCreatedIntegrationEvent>>(this);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _identity.DisposeAsync();
            await _academic.DisposeAsync();
        }
    }
}
