namespace EnglishCenter.Modules.MediaNotification.Contracts;

public interface IAvatarFileAccess
{
    Task<AvatarDownload?> GetConfirmedAsync(Guid ownerId, Guid fileId, CancellationToken ct);
}

public sealed record AvatarDownload(Uri Url, DateTimeOffset ExpiresAt);
