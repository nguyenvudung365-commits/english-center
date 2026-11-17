using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

internal sealed class StudentDisplayDirectory(IIdentityStore store) : IStudentDisplayDirectory
{
    public async Task<IReadOnlyList<StudentDisplay>> FindActiveAsync(IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        var result = new List<StudentDisplay>();
        foreach (var id in studentIds.Distinct())
            if (await store.FindUserAsync(id, ct) is { Role: Roles.Student, IsLocked: false } user)
                result.Add(new(user.Id, user.FullName, user.StudentCode));
        return result;
    }
}
