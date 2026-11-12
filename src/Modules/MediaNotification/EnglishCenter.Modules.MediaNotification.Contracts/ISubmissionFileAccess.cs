namespace EnglishCenter.Modules.MediaNotification.Contracts;

public sealed record ConfirmedSubmissionFile(Guid FileId, string FileName, string ContentType, long SizeBytes);
public sealed record SubmissionDownload(Uri Url, DateTimeOffset ExpiresAt);
// Internal application-to-application boundary. Callers must authorize the submission first.
public interface ISubmissionFileAccess
{
    Task<IReadOnlyList<ConfirmedSubmissionFile>?> GetConfirmedAsync(Guid ownerId, IReadOnlyList<Guid> fileIds, CancellationToken ct);
    Task<SubmissionDownload?> DownloadAsync(Guid ownerId, Guid fileId, CancellationToken ct);
}
