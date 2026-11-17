namespace EnglishCenter.Modules.Academic.Contracts;

public sealed record SessionIdentity(Guid Id, Guid ClassId);

// Internal lookup only: consumers must authorize callers and verify the class before linkage.
public interface ISessionDirectory
{
    Task<SessionIdentity?> FindSessionAsync(Guid sessionId, CancellationToken ct);
}
