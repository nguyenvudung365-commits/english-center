using EnglishCenter.Modules.MediaNotification.Contracts;

namespace EnglishCenter.Modules.MediaNotification.Application.Files;

internal sealed class SubmissionFileAccess(IFileStore store, IObjectStorage storage, TimeProvider clock) : ISubmissionFileAccess
{
    public async Task<IReadOnlyList<ConfirmedSubmissionFile>?> GetConfirmedAsync(Guid ownerId, IReadOnlyList<Guid> fileIds, CancellationToken ct)
    {
        if (ownerId == Guid.Empty || fileIds.Count is < 1 or > 5) return null;
        var result = new List<ConfirmedSubmissionFile>();
        foreach (var id in fileIds)
        {
            var file = await store.FindOwnedAsync(id, ownerId, ct);
            if (file is not { UploadedAt: not null, FileKey: not null, Purpose: "submission" }) return null;
            result.Add(new(file.Id, file.FileName, file.ContentType, file.SizeBytes));
        }
        return result;
    }
    public async Task<SubmissionDownload?> DownloadAsync(Guid ownerId, Guid fileId, CancellationToken ct)
    {
        var file = await store.FindOwnedAsync(fileId, ownerId, ct);
        if (file is not { UploadedAt: not null, FileKey: not null, Purpose: "submission" }) return null;
        var expiry = clock.GetUtcNow().AddMinutes(5);
        return new(await storage.PresignGetAsync(file.FileKey, file.FileName, expiry, ct), expiry);
    }
}
