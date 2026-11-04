using EnglishCenter.BuildingBlocks.Security;

namespace EnglishCenter.Api.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Principal is not null && Principal.TryGetUserId(out var id) ? id : null;

    public string? Role => IsAuthenticated ? Principal!.GetRole() : null;

    public bool IsInRole(string role) => Role == role;
}
