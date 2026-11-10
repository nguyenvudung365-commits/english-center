using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace EnglishCenter.IntegrationTests;

/// <summary>Chạy chỉ khi ENGLISHCENTER_TEST_POSTGRES có giá trị (chuỗi kết nối tới server, quyền tạo database). Nếu không, test được báo Skipped.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "ENGLISHCENTER_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Biến môi trường {Variable} chưa được đặt; chưa kiểm chứng trên PostgreSQL thật.";
    }
}

public sealed class IdentityPostgresTests : IAsyncLifetime
{
    private const string AdminEmail = "admin.pg@example.test";
    private const string AdminPassword = "Admin-password-123!";
    private const string UserPassword = "User-password-123!";

    private string _serverConnection = string.Empty;
    private string _database = string.Empty;
    private string DbConnection => new NpgsqlConnectionStringBuilder(_serverConnection) { Database = _database }.ConnectionString;

    public async Task InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(value)) return; // test sẽ Skipped
        _serverConnection = value;
        _database = "ec_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database.Length == 0) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    private WebApplicationFactory<Program> NewHost()
    {
        var host = new PgFactory(DbConnection);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var expected = new NpgsqlConnectionStringBuilder(DbConnection);
        var actual = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Equal(expected.Host, actual.Host);
        Assert.Equal(expected.Port, actual.Port);
        Assert.Equal(expected.Database, actual.Database);
        return host;
    }

    private static async Task<TokenResponse> Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static void Authorize(HttpClient client, TokenResponse token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

    [PostgresFact]
    public async Task F1_Migration_CreatesIdentitySchemaAndHistoryTable()
    {
        await using var host = NewHost();
        using var client = host.CreateClient();
        await Login(client, AdminEmail, AdminPassword);

        await using var connection = new NpgsqlConnection(DbConnection);
        await connection.OpenAsync();
        await using var history = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema='identity' and table_name='__EFMigrationsHistory'", connection);
        Assert.Equal(1L, await history.ExecuteScalarAsync());
        await using var schema = new NpgsqlCommand("select count(*) from information_schema.schemata where schema_name='identity'", connection);
        Assert.Equal(1L, await schema.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task F2_LoginRefreshLogoutChangePassword_WorkOnPostgres()
    {
        await using var host = NewHost();
        using var client = host.CreateClient();
        Authorize(client, await Login(client, AdminEmail, AdminPassword));
        var created = await client.PostAsJsonAsync("/api/identity/users",
            new CreateUserRequest("student.pg@example.test", "Student PG", UserPassword, "Student"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var first = await Login(client, "student.pg@example.test", UserPassword);
        var refreshed = await client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(first.RefreshToken))).StatusCode);

        Authorize(client, second);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/identity/auth/logout", new RefreshRequest(second.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(second.RefreshToken))).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var s1 = await Login(client, "student.pg@example.test", UserPassword);
        var s2 = await Login(client, "student.pg@example.test", UserPassword);
        Authorize(client, s1);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/identity/auth/change-password",
            new ChangePasswordRequest(UserPassword, "Changed-password-123!"))).StatusCode);
        foreach (var token in new[] { s1, s2 })
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/identity/auth/refresh", new RefreshRequest(token.RefreshToken))).StatusCode);
    }

    [PostgresFact]
    public async Task F3_ConcurrentCreateSameEmail_OneCreatedOneConflict_NoServerError()
    {
        await using var host = NewHost();
        using var client = host.CreateClient();
        Authorize(client, await Login(client, AdminEmail, AdminPassword));
        var request = new CreateUserRequest("race.pg@example.test", "Race", UserPassword, "Student");

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/identity/users", request)));

        var codes = responses.Select(x => x.StatusCode).OrderBy(x => (int)x).ToArray();
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], codes);
    }

    [PostgresFact]
    public async Task F4_DataSurvivesNewHostOnSameDatabase()
    {
        await using (var host = NewHost())
        {
            using var client = host.CreateClient();
            Authorize(client, await Login(client, AdminEmail, AdminPassword));
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/identity/users",
                new CreateUserRequest("persist.pg@example.test", "Persist", UserPassword, "Teacher"))).StatusCode);
        }

        await using var second = NewHost();
        using var again = second.CreateClient();
        var token = await Login(again, "persist.pg@example.test", UserPassword);
        Assert.False(string.IsNullOrEmpty(token.AccessToken));
    }

    private sealed class PgFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // DI đọc connection string khi đăng ký module, trước callback ConfigureAppConfiguration.
            builder.UseSetting("ConnectionStrings:Default", connection);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connection,
                ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Identity:InitializeDatabase"] = "true",
                ["Identity:BootstrapAdmin:Email"] = AdminEmail,
                ["Identity:BootstrapAdmin:FullName"] = "Admin PG",
                ["Identity:BootstrapAdmin:Password"] = AdminPassword
            }));
            builder.ConfigureTestServices(services =>
            {
                // Infrastructure captures its connection before ConfigureAppConfiguration runs.
                // Remove the captured options callback as well as the cached options registration.
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connection,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentitySchema.Name)));
            });
        }
    }
}
