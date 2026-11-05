using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

/// <summary>Tiêu chí C5: lỗi chưa xử lý trả 500 ProblemDetails, không lộ stack trace ở Production.</summary>
public sealed class ApiErrorContractTests
{
    private const string SecretMessage = "SECRET-INTERNAL-DETAIL-do-not-leak";

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task UnhandledException_Returns500ProblemDetails_WithoutStackTrace(string environment)
    {
        await using var factory = new ThrowingFactory(environment);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest("a@example.test", "password"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain(SecretMessage, raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain(" at ", raw);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".cs:line", raw);
        Assert.False(doc.RootElement.TryGetProperty("exception", out _));
    }

    private sealed class ThrowingStore : IIdentityStore
    {
        private static Exception Boom() => new InvalidOperationException(SecretMessage);
        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) => throw Boom();
        public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => throw Boom();
        public Task<RefreshSession?> FindSessionAsync(string hash, CancellationToken ct) => throw Boom();
        public Task<IReadOnlyList<RefreshSession>> SessionsAsync(Guid userId, CancellationToken ct) => throw Boom();
        public Task<IReadOnlyList<User>> UsersAsync(string? role, CancellationToken ct) => throw Boom();
        public void AddUser(User user) => throw Boom();
        public void AddSession(RefreshSession session) => throw Boom();
        public Task SaveAsync(CancellationToken ct) => throw Boom();
    }

    private sealed class ThrowingFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            // UseSetting: khóa phải có sẵn khi đăng ký module (Production không tự sinh khóa).
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Identity:InitializeDatabase", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIdentityStore>();
                services.AddScoped<IIdentityStore, ThrowingStore>();
            });
        }
    }
}
