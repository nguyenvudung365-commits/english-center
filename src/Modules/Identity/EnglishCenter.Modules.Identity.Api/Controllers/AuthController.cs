using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;

namespace EnglishCenter.Modules.Identity.Api.Controllers;

// Nullable and unvalidated so cookie mode can send `{}`; the service validates the legacy token.
public sealed record RefreshBody(string? RefreshToken);

[ApiController]
[Route("api/identity/auth")]
[IdentityErrors]
[EnableRateLimiting("identity-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IdentityService identity, IHostEnvironment env) : ControllerBase
{
    // Browser mode: the refresh token lives only in an HttpOnly cookie. The custom header is required (a cross-site
    // form or no-CORS request cannot send it), so an ambient cookie alone never authorises anything.
    private const string ModeHeader = "X-Session-Mode";
    private const string SessionHeader = "X-Expected-Session";
    private const string CookieName = "ec_refresh";

    private bool CookieMode => string.Equals(Request.Headers[ModeHeader].ToString(), "cookie", StringComparison.OrdinalIgnoreCase);

    // No Expires/Max-Age: a browser-session cookie. Host-only (no Domain) and scoped to the auth endpoints.
    // Secure everywhere except Development so a local http:// candidate still works.
    private CookieOptions CookieSettings => new()
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = !env.IsDevelopment(),
        Path = "/api/identity/auth", IsEssential = true
    };

    private Guid? ExpectedSession()
    {
        if (!Request.Headers.TryGetValue(SessionHeader, out var value)) return null;
        return Guid.TryParseExact(value.ToString(), "D", out var id) && id != Guid.Empty
            ? id : throw new IdentityFailure(400, "Invalid session identifier.");
    }

    private IActionResult BrowserTokens(TokenResponse tokens, Guid sessionId)
    {
        Response.Cookies.Append(CookieName, tokens.RefreshToken, CookieSettings);
        return Ok(new BrowserTokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, sessionId));
    }

    [AllowAnonymous, HttpPost("login")]
    [ProducesResponseType<TokenResponse>(200)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var (tokens, id) = await identity.LoginAsync(request, ct);
        return CookieMode ? BrowserTokens(tokens, id) : Ok(tokens);
    }

    [AllowAnonymous, HttpPost("refresh")]
    [ProducesResponseType<TokenResponse>(200)]
    public async Task<IActionResult> Refresh([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshBody? body, CancellationToken ct)
    {
        if (CookieMode)
        {
            var cookie = Request.Cookies[CookieName];
            if (string.IsNullOrEmpty(cookie)) throw new IdentityFailure(401, "Invalid credentials or refresh token.");
            var (tokens, id) = await identity.RefreshBrowserAsync(cookie, ExpectedSession(), ct);
            return BrowserTokens(tokens, id);
        }
        return MissingToken(body) ?? Ok(await identity.RefreshAsync(new RefreshRequest(body!.RefreshToken!), ct));
    }

    // Same 400 shape the model validator produced when the body was bound to RefreshRequest.
    private IActionResult? MissingToken(RefreshBody? body)
    {
        if (!string.IsNullOrEmpty(body?.RefreshToken)) return null;
        ModelState.AddModelError(nameof(RefreshRequest.RefreshToken), "The RefreshToken field is required.");
        return ValidationProblem(ModelState);
    }

    // Anonymous so an expired access token cannot block logout; the legacy path re-checks authentication itself.
    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshBody? body, CancellationToken ct)
    {
        if (CookieMode)
        {
            var expected = ExpectedSession();
            if (expected is null && Request.Headers.ContainsKey("Authorization") && User.Identity?.IsAuthenticated != true)
                return Challenge();
            await identity.RevokeSessionAsync(User.Identity?.IsAuthenticated == true ? CurrentUserId() : null,
                Request.Cookies[CookieName] ?? string.Empty, expected, ct);
            Response.Cookies.Delete(CookieName, CookieSettings);
            return NoContent();
        }
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (MissingToken(body) is { } invalid) return invalid;
        await identity.LogoutAsync(CurrentUserId(), new RefreshRequest(body!.RefreshToken!), ct);
        return NoContent();
    }

    [Authorize, HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await identity.ChangePasswordAsync(CurrentUserId(), request, ct);
        return NoContent();
    }

    private Guid CurrentUserId() => User.TryGetUserId(out var id)
        ? id : throw new IdentityFailure(401, "Invalid subject claim.");
}

[ApiController]
[Route("api/identity/users")]
[Authorize(Roles = Roles.AccountManagers)]
[IdentityErrors]
public sealed class UsersController(IdentityService identity, UserSyncService sync) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await identity.CreateUserAsync(request, ct);
        return StatusCode(201, user);
    }

    [Authorize(Roles = Roles.Admin), HttpPost("republish")]
    public Task<RepublishUsersResponse> Republish(RepublishUsersRequest request, CancellationToken ct) =>
        sync.RepublishAsync(request, ct);

    [Authorize(Roles = Roles.Admin), HttpGet("teachers")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IReadOnlyList<ManagedTeacher>> Teachers(CancellationToken ct) => identity.TeachersAsync(ct);

    [Authorize(Roles = Roles.Admin), HttpPut("teachers/{id:guid}")]
    public Task<ManagedTeacher> UpdateTeacher(Guid id, UpdateTeacherRequest request, CancellationToken ct) =>
        identity.UpdateTeacherAsync(id, request, ct);

    [Authorize(Roles = Roles.Admin), HttpPut("teachers/{id:guid}/lock")]
    public Task<ManagedTeacher> SetTeacherLock(Guid id, TeacherLockRequest request, CancellationToken ct) =>
        identity.SetTeacherLockAsync(id, request, ct);
}

public sealed class IdentityErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not IdentityFailure failure) return;
        ProblemDetails body = failure.Errors is { Count: > 0 } errors
            ? new ValidationProblemDetails(errors.ToDictionary(x => x.Key, x => x.Value))
            { Status = failure.Status, Title = failure.Message }
            : new ProblemDetails { Status = failure.Status, Title = failure.Message };
        body.Type = $"https://httpstatuses.io/{failure.Status}";
        body.Instance = context.HttpContext.Request.Path;
        context.Result = new ObjectResult(body) { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
