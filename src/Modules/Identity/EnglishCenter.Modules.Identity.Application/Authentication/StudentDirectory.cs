using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

internal sealed class StudentDirectory(IIdentityStore store) : IStudentDirectory
{
    public Task<bool> IsActiveStudentAsync(Guid userId, CancellationToken ct) =>
        store.IsActiveInRoleAsync(userId, Roles.Student, ct);
}
