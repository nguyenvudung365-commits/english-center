using EnglishCenter.Modules.MediaNotification.Domain.Files;

namespace EnglishCenter.Modules.MediaNotification.Application.Files;

public sealed record PresignUploadRequest(string FileName, string ContentType, long? SizeBytes, string Purpose);
public sealed record UploadTicket(Guid FileId, string FileKey, Uri Url, string Method,
    IReadOnlyDictionary<string, string> RequiredHeaders, DateTimeOffset ExpiresAt);
public sealed record FileResponse(Guid FileId, string FileKey, string FileName, string ContentType,
    long SizeBytes, DateTimeOffset UploadedAt);
public sealed record DownloadTicket(Uri Url, DateTimeOffset ExpiresAt);
public sealed record ObjectInfo(long SizeBytes, string ContentType, string ETag);

public interface IFileStore
{
    Task AddAsync(StoredFile file, CancellationToken ct);
    Task<StoredFile?> FindOwnedAsync(Guid fileId, Guid ownerId, CancellationToken ct);
    Task<bool> TryCompleteAsync(Guid fileId, Guid ownerId, Guid revision, string fileKey,
        DateTimeOffset uploadedAt, CancellationToken ct);
    Task<bool> IsReferencedAsync(string fileKey, CancellationToken ct);
}

public interface IObjectStorage
{
    Task<Uri> PresignPutAsync(string key, string contentType, DateTimeOffset expiresAt, CancellationToken ct);
    Task<Uri> PresignGetAsync(string key, string fileName, DateTimeOffset expiresAt, CancellationToken ct);
    Task<ObjectInfo?> HeadAsync(string key, CancellationToken ct);
    Task CopyAsync(string sourceKey, string destinationKey, string sourceETag, string contentType, CancellationToken ct);
    Task<byte[]> ReadAsync(string key, int maxBytes, CancellationToken ct);
    // Best effort: implementations must log cleanup failures; cancellation must propagate.
    Task DeleteAsync(string key, CancellationToken ct);
}

public sealed class FileFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
