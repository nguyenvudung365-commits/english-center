using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.Security;
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

// Hợp đồng đề xuất: header opt-in "X-Session-Mode: cookie" trên /api/identity/auth/{login,refresh,logout}.
// Chế độ cookie: refresh token nằm trong cookie HttpOnly + SameSite=Strict (cookie phiên trình duyệt,
// không Expires/Max-Age), JSON không chứa refresh token; refresh/logout không cần body (hoặc body {}).
// Không có header => hành vi JSON cũ giữ nguyên. Tên cookie không được cố định trong hợp đồng nên test
// không đoán tên: lấy từ Set-Cookie của login.
public sealed class IdentityCookieSessionTests : IAsyncLifetime
{
    private const string Password = "Test-password-123!";
    private const string Mode = "X-Session-Mode";
    private readonly CookieAuthFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // HandleCookies=false: test tự quản lý Cookie header, không phụ thuộc Secure/CookieContainer.
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureCreatedAsync();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        foreach (var role in new[] { Roles.Student, Roles.Teacher })
            db.Users.Add(User.Create($"{role}@example.test", role, role, passwords.Hash(Password), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() { _client.Dispose(); await _factory.DisposeAsync(); }

    // ---- Login (cookie mode) ----

    [Fact]
    public async Task CookieLogin_SetsHttpOnlyStrictSessionCookie_AndOmitsRefreshTokenFromJson()
    {
        var response = await Post("/api/identity/auth/login", Credentials("Student"), cookieMode: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = SessionCookie(response);
        Assert.False(string.IsNullOrEmpty(cookie.Value));
        Assert.True(cookie.Has("httponly"));
        Assert.Equal("strict", cookie.Attribute("samesite")?.ToLowerInvariant());
        Assert.Null(cookie.Attribute("expires"));   // cookie phiên trình duyệt: hết khi đóng trình duyệt
        Assert.Null(cookie.Attribute("max-age"));

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(cookie.Value, raw);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.False(string.IsNullOrEmpty(Str(body, "accessToken")));
        Assert.True(string.IsNullOrEmpty(Str(body, "refreshToken")));
    }

    [Fact]
    public async Task CookieLogin_InvalidCredentials_Returns401_AndSetsNoCookie()
    {
        var response = await Post("/api/identity/auth/login",
            new { email = "Student@example.test", password = "incorrect" }, cookieMode: true);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    // ---- Refresh (cookie mode) ----

    [Theory]
    [InlineData(true)]   // body {}
    [InlineData(false)]  // không body
    public async Task CookieRefresh_UsesCookieOnly_Rotates_AndRefusesPreviousToken(bool sendEmptyObject)
    {
        var (_, first) = await CookieLogin("Student");

        var refreshed = await CookieCall("/api/identity/auth/refresh", first, sendEmptyObject);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = SessionCookie(refreshed);
        Assert.Equal(first.Name, second.Name);
        Assert.NotEqual(first.Value, second.Value);
        Assert.True(second.Has("httponly"));
        Assert.Equal("strict", second.Attribute("samesite")?.ToLowerInvariant());
        Assert.Null(second.Attribute("expires"));
        Assert.Null(second.Attribute("max-age"));

        var raw = await refreshed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(second.Value, raw);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.False(string.IsNullOrEmpty(Str(body, "accessToken")));
        Assert.True(string.IsNullOrEmpty(Str(body, "refreshToken")));

        // token đã xoay vòng không được dùng lại
        Assert.Equal(HttpStatusCode.Unauthorized, (await CookieCall("/api/identity/auth/refresh", first, true)).StatusCode);
        // token mới vẫn dùng được
        Assert.Equal(HttpStatusCode.OK, (await CookieCall("/api/identity/auth/refresh", second, true)).StatusCode);
    }

    [Fact]
    public async Task CookieRefresh_WithoutCookie_Returns401()
    {
        var response = await Post("/api/identity/auth/refresh", new { }, cookieMode: true);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CookieRefresh_UnknownCookieValue_Returns401()
    {
        var (_, real) = await CookieLogin("Student");
        var forged = real with { Value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await CookieCall("/api/identity/auth/refresh", forged, true)).StatusCode);
    }

    // ---- Logout (cookie mode) ----

    [Fact]
    public async Task CookieLogout_AfterReload_WithoutAccessToken_RevokesSession_AndClearsCookie()
    {
        // "Tải lại trang": access token trong bộ nhớ mất, chỉ còn cookie phiên.
        var (_, cookie) = await CookieLogin("Student");
        var reloaded = await CookieCall("/api/identity/auth/refresh", cookie, true);
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var current = SessionCookie(reloaded);

        var logout = await CookieCall("/api/identity/auth/logout", current, true); // không Authorization
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var cleared = SessionCookie(logout);
        Assert.Equal(current.Name, cleared.Name);
        Assert.True(cleared.Value.Length == 0 || IsExpired(cleared));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CookieCall("/api/identity/auth/refresh", current, true)).StatusCode);
    }

    [Fact]
    public async Task CookieLogout_WithBearerAndNoBody_RevokesSession()
    {
        var (token, cookie) = await CookieLogin("Student");
        var request = CookieRequest("/api/identity/auth/logout", cookie, sendEmptyObject: false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CookieCall("/api/identity/auth/refresh", cookie, true)).StatusCode);
    }

    [Fact]
    public async Task CookieLogout_RevokesOnlyThatSession()
    {
        var (_, one) = await CookieLogin("Student");
        var (_, two) = await CookieLogin("Student");
        Assert.Equal(HttpStatusCode.NoContent, (await CookieCall("/api/identity/auth/logout", one, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CookieCall("/api/identity/auth/refresh", one, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CookieCall("/api/identity/auth/refresh", two, true)).StatusCode);
    }

    [Fact]
    public async Task CookieLogout_WithBearerOfAnotherUser_CannotRevokeCookieSession()
    {
        var (_, student) = await CookieLogin("Student");
        var teacher = await LegacyLogin("Teacher");
        var request = CookieRequest("/api/identity/auth/logout", student, sendEmptyObject: true);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", teacher.AccessToken);
        var result = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.False(result.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, (await CookieCall("/api/identity/auth/refresh", student, true)).StatusCode);
    }

    [Fact]
    public async Task CookieSession_StaleTabCannotRefreshOrLogoutNewLogin_AndIdSurvivesRotation()
    {
        var older = await Post("/api/identity/auth/login", Credentials("Student"), cookieMode: true);
        var current = await Post("/api/identity/auth/login", Credentials("Student"), cookieMode: true);
        var oldId = (await older.Content.ReadFromJsonAsync<BrowserTokenResponse>())!.SessionId;
        var currentId = (await current.Content.ReadFromJsonAsync<BrowserTokenResponse>())!.SessionId;
        Assert.NotEqual(oldId, currentId);
        var cookie = SessionCookie(current);
        foreach (var action in new[] { "refresh", "logout" })
        {
            using var request = CookieRequest($"/api/identity/auth/{action}", cookie, true);
            request.Headers.Add("X-Expected-Session", oldId.ToString());
            using var result = await _client.SendAsync(request); // No bearer required for the binding.
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.False(result.Headers.Contains("Set-Cookie"));
        }

        var rotated = await CookieCall("/api/identity/auth/refresh", cookie, true);
        Assert.Equal(currentId, (await rotated.Content.ReadFromJsonAsync<BrowserTokenResponse>())!.SessionId);
        using var logout = CookieRequest("/api/identity/auth/logout", SessionCookie(rotated), true);
        logout.Headers.Add("X-Expected-Session", currentId.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await CookieCall("/api/identity/auth/refresh", SessionCookie(rotated), true)).StatusCode);
    }

    [Fact]
    public async Task CookieLogout_WithoutCookieAndWithoutBearer_IsRejected_NotServerError()
    {
        var response = await Post("/api/identity/auth/logout", new { }, cookieMode: true);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Header opt-in: không header thì không dùng cookie ----

    [Fact]
    public async Task NoHeader_RefreshIgnoresCookie_AndDoesNotRotateCookieSession()
    {
        var (_, cookie) = await CookieLogin("Student");

        var withEmptyBody = await Post("/api/identity/auth/refresh", new { }, cookie: cookie);
        Assert.True(withEmptyBody.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
            $"Got {(int)withEmptyBody.StatusCode}");
        Assert.False(withEmptyBody.Headers.Contains("Set-Cookie"));
        var raw = await withEmptyBody.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", raw, StringComparison.OrdinalIgnoreCase);

        // cookie chưa bị tiêu thụ
        Assert.Equal(HttpStatusCode.OK, (await CookieCall("/api/identity/auth/refresh", cookie, true)).StatusCode);
    }

    [Fact]
    public async Task NoHeader_LogoutIgnoresCookie_AndDoesNotRevokeCookieSession()
    {
        var (token, cookie) = await CookieLogin("Student");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/logout") { Content = Json(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Cookie", cookie.Header);
        Assert.NotEqual(HttpStatusCode.NoContent, (await _client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CookieCall("/api/identity/auth/refresh", cookie, true)).StatusCode);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("")]
    [InlineData("Cookie2")]
    public async Task UnknownHeaderValue_DoesNotEnableCookieMode(string value)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/login") { Content = Json(Credentials("Student")) };
        request.Headers.TryAddWithoutValidation(Mode, value);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.False(string.IsNullOrEmpty(Str(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement, "refreshToken")));
    }

    // ---- Legacy JSON không đổi ----

    [Fact]
    public async Task Legacy_Login_ReturnsRefreshTokenInJson_AndSetsNoCookie()
    {
        var response = await Post("/api/identity/auth/login", Credentials("Student"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.False(string.IsNullOrEmpty(token.AccessToken));
        Assert.False(string.IsNullOrEmpty(token.RefreshToken));
    }

    [Fact]
    public async Task Legacy_RefreshAndLogout_StillWorkWithJsonBody_AndSetNoCookie()
    {
        var original = await LegacyLogin("Student");

        var refreshed = await Post("/api/identity/auth/refresh", new RefreshRequest(original.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.False(refreshed.Headers.Contains("Set-Cookie"));
        var replacement = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(original.RefreshToken, replacement.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/identity/auth/refresh", new RefreshRequest(original.RefreshToken))).StatusCode);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/logout") { Content = Json(new RefreshRequest(replacement.RefreshToken)) };
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacement.AccessToken);
        var logoutResponse = await _client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.False(logoutResponse.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/identity/auth/refresh", new RefreshRequest(replacement.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task Legacy_Logout_WithoutBearer_StillReturns401()
    {
        var legacy = await LegacyLogin("Student");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/identity/auth/logout", new RefreshRequest(legacy.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/identity/auth/refresh", new RefreshRequest(legacy.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task Legacy_Refresh_WithStrayCookie_UsesBodyToken()
    {
        var (_, cookie) = await CookieLogin("Student");
        var legacy = await LegacyLogin("Student");
        var response = await Post("/api/identity/auth/refresh", new RefreshRequest(legacy.RefreshToken), cookie: cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        // cookie của phiên khác không bị đụng tới
        Assert.Equal(HttpStatusCode.OK, (await CookieCall("/api/identity/auth/refresh", cookie, true)).StatusCode);
    }

    [Fact]
    public async Task Legacy_EmptyRefreshBody_StillReturns400WithTokenError()
    {
        var response = await Post("/api/identity/auth/refresh", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ---- Helpers ----

    private sealed record Cookie(string Name, string Value, string[] Attributes)
    {
        public string Header => $"{Name}={Value}";
        public bool Has(string attribute) => Attributes.Any(x => x.Equals(attribute, StringComparison.OrdinalIgnoreCase));
        public string? Attribute(string name) => Attributes
            .Select(x => x.Split('=', 2))
            .Where(x => x[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Length > 1 ? x[1] : "")
            .FirstOrDefault();
    }

    private static Cookie SessionCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values), "Missing Set-Cookie");
        var parsed = values!.Select(v =>
        {
            var parts = v.Split(';').Select(x => x.Trim()).ToArray();
            var pair = parts[0].Split('=', 2);
            return new Cookie(pair[0], pair.Length > 1 ? pair[1] : "", parts.Skip(1).ToArray());
        }).ToArray();
        return Assert.Single(parsed);
    }

    private static bool IsExpired(Cookie cookie) =>
        cookie.Attribute("max-age") is { } age && int.TryParse(age, out var seconds) && seconds <= 0
        || cookie.Attribute("expires") is { } expires && DateTimeOffset.TryParse(expires, out var at) && at < DateTimeOffset.UtcNow;

    private static object Credentials(string role) => new { email = $"{role}@example.test", password = Password };
    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    private static string? Str(JsonElement e, string name) => e.EnumerateObject()
        .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        .Select(p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null).FirstOrDefault();

    private Task<HttpResponseMessage> Post(string url, object body, bool cookieMode = false, Cookie? cookie = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = Json(body) };
        if (cookieMode) request.Headers.Add(Mode, "cookie");
        if (cookie is not null) request.Headers.Add("Cookie", cookie.Header);
        return _client.SendAsync(request);
    }

    private HttpRequestMessage CookieRequest(string url, Cookie cookie, bool sendEmptyObject)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (sendEmptyObject) request.Content = Json(new { });
        request.Headers.Add(Mode, "cookie");
        request.Headers.Add("Cookie", cookie.Header);
        return request;
    }

    private Task<HttpResponseMessage> CookieCall(string url, Cookie cookie, bool sendEmptyObject) =>
        _client.SendAsync(CookieRequest(url, cookie, sendEmptyObject));

    private async Task<(string AccessToken, Cookie Cookie)> CookieLogin(string role)
    {
        var response = await Post("/api/identity/auth/login", Credentials(role), cookieMode: true);
        response.EnsureSuccessStatusCode();
        var access = Str(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement, "accessToken")!;
        return (access, SessionCookie(response));
    }

    private async Task<TokenResponse> LegacyLogin(string role)
    {
        var response = await Post("/api/identity/auth/login", Credentials(role));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private sealed class CookieAuthFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

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
                services.RemoveAll<IStudentCodeGenerator>();
                services.AddSingleton<IStudentCodeGenerator, Codes>();
            });
        }

        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class Codes : IStudentCodeGenerator
    {
        private int sequence;
        public Task<string> AllocateAsync(DateTimeOffset createdAt, CancellationToken ct) =>
            Task.FromResult($"HV{Interlocked.Increment(ref sequence):D6}-{createdAt.UtcDateTime:yy}");
    }
}
