using EnglishCenter.Modules.Academic.Contracts;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

internal sealed class SessionDirectory(ISessionStore store) : ISessionDirectory
{
    public async Task<SessionIdentity?> FindSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var item = await store.FindAsync(sessionId, ct);
        return item is null ? null : new(item.Id, item.ClassId);
    }
}
