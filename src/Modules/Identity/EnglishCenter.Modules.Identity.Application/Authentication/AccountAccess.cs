using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

public sealed class AccountAccess(IIdentityStore store) : IAccountAccess
{
    public Task<bool> IsActiveInRoleAsync(Guid userId, string? expectedRole, CancellationToken ct) =>
        store.IsActiveInRoleAsync(userId, expectedRole, ct);
}
