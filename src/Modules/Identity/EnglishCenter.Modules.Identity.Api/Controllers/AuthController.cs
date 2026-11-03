using System.Security.Claims;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace EnglishCenter.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/identity/auth")]
[IdentityErrors]
[EnableRateLimiting("identity-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IdentityService identity) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")]
    public Task<TokenResponse> Login(LoginRequest request, CancellationToken ct) => identity.LoginAsync(request, ct);

    [AllowAnonymous, HttpPost("refresh")]
    public Task<TokenResponse> Refresh(RefreshRequest request, CancellationToken ct) => identity.RefreshAsync(request, ct);

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await identity.LogoutAsync(CurrentUserId(), request, ct);
        return NoContent();
    }

    [Authorize, HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await identity.ChangePasswordAsync(CurrentUserId(), request, ct);
        return NoContent();
    }

    private Guid CurrentUserId() => Guid.TryParse(User.FindFirstValue(AuthClaims.Subject), out var id)
        ? id : throw new IdentityFailure(401, "Invalid subject claim.");
}

[ApiController]
[Route("api/identity/users")]
[Authorize(Roles = Roles.AccountManagers)]
[IdentityErrors]
public sealed class UsersController(IdentityService identity) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await identity.CreateUserAsync(request, ct);
        return StatusCode(201, user);
    }
}

public sealed class IdentityErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not IdentityFailure failure) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = failure.Status, Title = failure.Message })
        { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
