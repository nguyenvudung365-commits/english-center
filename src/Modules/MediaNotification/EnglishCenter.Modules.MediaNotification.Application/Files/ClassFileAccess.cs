using EnglishCenter.Modules.MediaNotification.Contracts;

namespace EnglishCenter.Modules.MediaNotification.Application.Files;

internal sealed class ClassFileAccess(IFileStore store, IObjectStorage storage, TimeProvider clock) : IClassFileAccess
{
    private async Task<EnglishCenter.Modules.MediaNotification.Domain.Files.StoredFile?> FindAsync(Guid ownerId, Guid fileId, string purpose,
        CancellationToken ct)
    {
        if (ownerId == Guid.Empty || fileId == Guid.Empty || !FileService.IsClassFilePurpose(purpose)) return null;
        var file = await store.FindOwnedAsync(fileId, ownerId, ct);
        return file is { UploadedAt: not null, FileKey: not null } && file.Purpose == purpose ? file : null;
    }

    public async Task<ClassFileInfo?> GetConfirmedAsync(Guid ownerId, Guid fileId, string purpose, CancellationToken ct) =>
        await FindAsync(ownerId, fileId, purpose, ct) is { } file ? new(file.Id, file.FileName, file.ContentType, file.SizeBytes) : null;

    public async Task<ClassFileDownload?> DownloadAsync(Guid ownerId, Guid fileId, string purpose, CancellationToken ct)
    {
        if (await FindAsync(ownerId, fileId, purpose, ct) is not { } file) return null;
        var expiry = clock.GetUtcNow().AddMinutes(5);
        return new(await storage.PresignGetAsync(file.FileKey!, file.FileName, expiry, ct), expiry);
    }
}
