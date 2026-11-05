namespace EnglishCenter.Modules.Identity.Domain.Users;

public sealed class RefreshSession
{
    private RefreshSession() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshSession Create(Guid userId, string hash, DateTimeOffset expiresAt) =>
        new() { Id = Guid.NewGuid(), UserId = userId, TokenHash = hash, ExpiresAt = expiresAt };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
