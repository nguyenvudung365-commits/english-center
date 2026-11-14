using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

public sealed class AccountAccess(IIdentityStore store) : IAccountAccess
{
    public async Task<bool> IsActiveInRoleAsync(Guid userId, string? expectedRole, CancellationToken ct)
    {
        var user = await store.FindUserAsync(userId, ct);
        return user is { IsLocked: false } && user.Role == expectedRole;
    }
}
