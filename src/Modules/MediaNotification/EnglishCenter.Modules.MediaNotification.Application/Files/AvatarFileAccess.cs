using EnglishCenter.Modules.MediaNotification.Contracts;

namespace EnglishCenter.Modules.MediaNotification.Application.Files;

internal sealed class AvatarFileAccess(IFileStore store, IObjectStorage storage, TimeProvider clock) : IAvatarFileAccess
{
    public async Task<AvatarDownload?> GetConfirmedAsync(Guid ownerId, Guid fileId, CancellationToken ct)
    {
        if (ownerId == Guid.Empty || fileId == Guid.Empty) return null;
        var file = await store.FindOwnedAsync(fileId, ownerId, ct);
        if (file is not { Purpose: "avatar", UploadedAt: not null, FileKey: not null } ||
            !AvatarPolicy.IsValid(file.FileName, file.ContentType, file.SizeBytes)) return null;
        var expiry = clock.GetUtcNow().AddMinutes(5);
        return new(await storage.PresignGetAsync(file.FileKey, file.FileName, expiry, ct), expiry);
    }
}
