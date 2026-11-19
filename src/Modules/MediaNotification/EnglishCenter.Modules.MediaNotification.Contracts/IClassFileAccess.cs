namespace EnglishCenter.Modules.MediaNotification.Contracts;

public sealed record ClassFileInfo(Guid FileId, string FileName, string ContentType, long SizeBytes);
public sealed record ClassFileDownload(Uri Url, DateTimeOffset ExpiresAt);

// Internal application-to-application boundary for teacher-uploaded class files (purpose "material" or "exam").
// ownerId is the uploader; any other purpose, owner or unconfirmed file yields null. Callers must authorize the class first.
public interface IClassFileAccess
{
    Task<ClassFileInfo?> GetConfirmedAsync(Guid ownerId, Guid fileId, string purpose, CancellationToken ct);
    Task<ClassFileDownload?> DownloadAsync(Guid ownerId, Guid fileId, string purpose, CancellationToken ct);
}
