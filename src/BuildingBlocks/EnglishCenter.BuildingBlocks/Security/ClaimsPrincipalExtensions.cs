using System.Security.Claims;

namespace EnglishCenter.BuildingBlocks.Security;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        return principal.Identity?.IsAuthenticated == true
            && Guid.TryParse(principal.FindFirst(AuthClaims.Subject)?.Value, out userId);
    }

    public static string? GetRole(this ClaimsPrincipal principal) => principal.FindFirst(AuthClaims.Role)?.Value;

    public static bool IsStaffOrAdmin(this ClaimsPrincipal principal) =>
        principal.GetRole() is Roles.Staff or Roles.Admin;
}
