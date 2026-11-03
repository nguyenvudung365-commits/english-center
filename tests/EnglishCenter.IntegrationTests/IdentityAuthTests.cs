using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
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

public sealed class IdentityAuthTests : IAsyncLifetime
{
    private const string Password = "Test-password-123!";
    private readonly AuthFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Student, Roles.Teacher })
            db.Users.Add(User.Create($"{role}@example.test", role, role, passwords.Hash(Password), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { _client.Dispose(); await _factory.DisposeAsync(); }

    [Fact]
    public async Task Login_ReturnsConfiguredClaims_AndStoresOnlyHashes()
    {
        var response = await Login("Student");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.Equal(Roles.Student, jwt.Claims.Single(x => x.Type == AuthClaims.Role).Value);
        Assert.True(Guid.TryParse(jwt.Subject, out _));
        Assert.InRange(response.AccessTokenExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.DoesNotContain(response.RefreshToken, (await db.RefreshSessions.SingleAsync()).TokenHash);
        Assert.NotEqual(Password, (await db.Users.SingleAsync(x => x.Role == Roles.Student)).PasswordHash);
    }

    [Fact]
    public async Task InvalidLogin_IsGeneric_AndLockedAccountReturns403()
    {
        var wrong = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest("Student@example.test", "incorrect"));
        var missing = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest("missing@example.test", "incorrect"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Users.SingleAsync(x => x.Role == Roles.Student)).SetLocked(true);
        await db.SaveChangesAsync();
        var locked = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest("Student@example.test", Password));
        Assert.Equal(HttpStatusCode.Forbidden, locked.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndLogoutRevokesReplacement()
    {
        var original = await Login("Student");
        var refreshed = await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(original.RefreshToken));
        refreshed.EnsureSuccessStatusCode();
        var replacement = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(original.RefreshToken, replacement.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(original.RefreshToken))).StatusCode);
        Authorize(replacement);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync("/api/identity/auth/logout", new RefreshRequest(replacement.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(replacement.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ValidatesOldAndNew_AndRevokesAllSessions()
    {
        var first = await Login("Student");
        var second = await Login("Student");
        Authorize(first);
        foreach (var request in new[]
        {
            new ChangePasswordRequest("wrong", "New-password-123!"),
            new ChangePasswordRequest(Password, Password),
            new ChangePasswordRequest(Password, "short")
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/identity/auth/change-password", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync("/api/identity/auth/change-password",
            new ChangePasswordRequest(Password, "New-password-123!"))).StatusCode);
        foreach (var token in new[] { first, second })
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(token.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/login",
            new LoginRequest("Student@example.test", Password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/identity/auth/login",
            new LoginRequest("Student@example.test", "New-password-123!"))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Staff", HttpStatusCode.Created)]
    [InlineData("Teacher", HttpStatusCode.Forbidden)]
    [InlineData("Student", HttpStatusCode.Forbidden)]
    public async Task CreateAccount_RequiresAccountManager(string role, HttpStatusCode expected)
    {
        var request = new CreateUserRequest("new@example.test", "New User", Password, Roles.Student);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/users", request)).StatusCode);
        Authorize(await Login(role));
        var response = await _client.PostAsJsonAsync("/api/identity/users", request);
        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.Created) return;
        var user = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var registered = Assert.Single(_factory.Registered.Events);
        Assert.Equal(user.Id, registered.UserId);
        Assert.Equal(request.Role, registered.Role);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/identity/users",
            request with { Email = "NEW@example.test" })).StatusCode);
    }

    [Theory]
    [InlineData("Admin", "name@example.test", "User", "Valid-password-123!")]
    [InlineData("Student", "invalid", "User", "Valid-password-123!")]
    [InlineData("Student", "name@example.test", "   ", "Valid-password-123!")]
    [InlineData("Student", "name@example.test", "User", "short")]
    public async Task CreateAccount_RejectsInvalidInputAndPrivilegeEscalation(string role, string email, string name, string password)
    {
        Authorize(await Login("Staff"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/identity/users",
            new CreateUserRequest(email, name, password, role))).StatusCode);
    }

    [Fact]
    public async Task Logout_CannotRevokeOtherUsersSession()
    {
        var student = await Login("Student");
        Authorize(await Login("Teacher"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/logout", new RefreshRequest(student.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(student.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task ExpiredRefresh_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var user = await db.Users.FirstAsync();
        db.RefreshSessions.Add(RefreshSession.Create(user.Id, tokens.HashRefreshToken("expired"), DateTimeOffset.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest("expired"))).StatusCode);
    }

    [Fact]
    public async Task Revocation_UsesOptimisticConcurrency()
    {
        var login = await Login("Student");
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var a = await first.RefreshSessions.SingleAsync();
        var b = await second.RefreshSessions.SingleAsync();
        a.Revoke(DateTimeOffset.UtcNow);
        b.Revoke(DateTimeOffset.UtcNow);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent_AndDoesNotOverwritePassword()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IdentityService>();
        await service.BootstrapAdminAsync("Admin@example.test", "Admin", "Ignored-password-123!", default);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest("Admin@example.test", Password))).StatusCode);
        await Assert.ThrowsAsync<IdentityFailure>(() => service.BootstrapAdminAsync("Student@example.test", "Admin", Password, default));
    }

    private void Authorize(TokenResponse token) => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    [Fact]
    public async Task InvalidOrExpiredBearer_CannotCreateAccounts()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdentityStore>();
        var issuer = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var admin = (await store.FindByEmailAsync(User.NormalizeEmail("Admin@example.test"), default))!;
        var expired = issuer.Issue(admin, DateTimeOffset.UtcNow.AddHours(-2));
        foreach (var token in new[] { expired.AccessToken, "invalid.token.signature" })
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/users",
                new CreateUserRequest("new@example.test", "User", Password, Roles.Student))).StatusCode);
        }
    }

    [Fact]
    public async Task AuthEndpoints_RejectExcessiveAttempts()
    {
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest("unknown"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest("unknown"))).StatusCode);
    }

    private async Task<TokenResponse> Login(string role)
    {
        var response = await _client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest($"{role}@example.test", Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public RegistrationRecorder Registered { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _connection.Open();
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Identity:InitializeDatabase"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(o => o.UseSqlite(_connection));
                services.AddSingleton<IIntegrationEventHandler<UserRegisteredIntegrationEvent>>(Registered);
            });
        }

        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class RegistrationRecorder : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
    {
        public List<UserRegisteredIntegrationEvent> Events { get; } = [];
        public Task HandleAsync(UserRegisteredIntegrationEvent integrationEvent, CancellationToken ct = default)
        { Events.Add(integrationEvent); return Task.CompletedTask; }
    }
}
