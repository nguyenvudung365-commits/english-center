using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

internal sealed class UserDisplayDirectory(IIdentityStore store) : IUserDisplayDirectory
{
    public Task<IReadOnlyDictionary<Guid, string>> FindNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        store.FullNamesAsync(userIds.Distinct().ToList(), ct);
}
