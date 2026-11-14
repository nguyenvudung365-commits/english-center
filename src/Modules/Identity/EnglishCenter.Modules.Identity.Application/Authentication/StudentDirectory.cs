using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

internal sealed class StudentDirectory(IIdentityStore store) : IStudentDirectory
{
    public async Task<bool> IsActiveStudentAsync(Guid userId, CancellationToken ct)
    {
        var user = await store.FindUserAsync(userId, ct);
        return user is { Role: Roles.Student, IsLocked: false };
    }
}
