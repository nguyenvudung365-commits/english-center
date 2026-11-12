namespace EnglishCenter.Modules.Identity.Contracts;

public interface IAccountAccess
{
    Task<bool> IsActiveInRoleAsync(Guid userId, string? expectedRole, CancellationToken ct);
}
