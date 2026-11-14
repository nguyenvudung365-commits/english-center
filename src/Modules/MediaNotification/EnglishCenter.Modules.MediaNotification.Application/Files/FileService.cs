using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.MediaNotification.Application.Files;

public sealed class FileService(IFileStore store, IObjectStorage storage, ICurrentUser currentUser, TimeProvider timeProvider,
    IAccountAccess? accounts = null)
{
    public const int MaxSizeBytes = 20 * 1024 * 1024;
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".jpg"] = "image/jpeg", [".png"] = "image/png"
    };

    public async Task<UploadTicket> PresignUploadAsync(PresignUploadRequest request, CancellationToken ct = default)
    {
        var owner = Owner();
        if (request is null || string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > 255 ||
            request.FileName.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '"' or '<' or '>' or '|' or '?' or '*') ||
            request.FileName != request.FileName.Trim() || request.SizeBytes is not (> 0 and <= MaxSizeBytes) ||
            request.Purpose is not ("submission" or "avatar") ||
            (request.Purpose == "avatar" && !AvatarPolicy.IsValid(request.FileName, request.ContentType, request.SizeBytes.Value)) ||
            !ContentTypes.TryGetValue(Path.GetExtension(request.FileName), out var canonicalType) ||
            !string.Equals(canonicalType, request.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new FileFailure(400, "Invalid filename, content type, purpose or size (submission: 20 MiB; avatar: JPG/PNG, 2 MiB).");

        if (request.Purpose == "avatar") await RequireActiveAvatarOwnerAsync(owner, ct);

        var now = timeProvider.GetUtcNow();
        var file = StoredFile.Create(owner, request.FileName, canonicalType, request.SizeBytes.Value,
            request.Purpose, now, now.Add(TicketLifetime));
        // Metadata must exist before any upload capability is issued.
        await store.AddAsync(file, ct);
        var url = await storage.PresignPutAsync(file.StagingKey, file.ContentType, file.ExpiresAt, ct);
        return new UploadTicket(file.Id, file.StagingKey, url, "PUT",
            new Dictionary<string, string> { ["Content-Type"] = file.ContentType }, file.ExpiresAt);
    }

    public async Task<FileResponse> ConfirmUploadAsync(Guid fileId, CancellationToken ct = default)
    {
        var owner = Owner();
        var file = await FindAsync(fileId, owner, ct);
        if (file.Purpose == "avatar")
        {
            await RequireActiveAvatarOwnerAsync(owner, ct);
            if (!AvatarPolicy.IsValid(file.FileName, file.ContentType, file.SizeBytes))
                throw new FileFailure(400, "Invalid avatar type or size.");
        }
        if (file.UploadedAt is not null) return Response(file);
        CheckExpiry(file);
        var staged = await storage.HeadAsync(file.StagingKey, ct)
            ?? throw new FileFailure(409, "Upload has not arrived.");
        CheckObject(file, staged);
        if (string.IsNullOrWhiteSpace(staged.ETag)) throw new FileFailure(409, "Upload has no stable object version.");

        // Every attempt owns a unique candidate; no PUT capability ever points at accepted content.
        var candidate = $"files/{Guid.NewGuid():N}";
        var cleanupSafe = true;
        try
        {
            await storage.CopyAsync(file.StagingKey, candidate, staged.ETag, file.ContentType, ct);
            var final = await storage.HeadAsync(candidate, ct)
                ?? throw new FileFailure(503, "File storage is temporarily unavailable.");
            CheckObject(file, final);
            var content = await storage.ReadAsync(candidate, file.Purpose == "avatar" ? AvatarPolicy.MaxSizeBytes : MaxSizeBytes, ct);
            if (content.LongLength != file.SizeBytes || !FileContentValidator.IsValid(file.FileName, content))
                throw new FileFailure(400, "File content does not match its declared format or size.");
            CheckExpiry(file);
            // A lost acknowledgement or cancellation does not prove that the update rolled back.
            // An MVCC reference check cannot fence a commit still in flight, so retain this
            // candidate unless persistence returns a definitive rejection.
            cleanupSafe = false;
            var completed = await store.TryCompleteAsync(file.Id, owner, file.Revision, candidate, timeProvider.GetUtcNow(), ct);
            cleanupSafe = !completed;
            // A concurrent confirmation may have won. Always reload the authoritative pointer.
            var accepted = await FindAsync(fileId, owner, ct);
            if (accepted.UploadedAt is null) throw new FileFailure(409, "File changed concurrently. Retry confirmation.");
            return Response(accepted);
        }
        finally
        {
            if (cleanupSafe) await CleanupUnreferencedAsync(candidate);
        }
    }

    public async Task<DownloadTicket> PresignDownloadAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await FindAsync(fileId, Owner(), ct);
        if (file.Purpose == "avatar") await RequireActiveAvatarOwnerAsync(file.OwnerId, ct);
        if (file.UploadedAt is null || file.FileKey is null) throw new FileFailure(409, "File has not been confirmed.");
        var expiry = timeProvider.GetUtcNow().Add(TicketLifetime);
        var url = await storage.PresignGetAsync(file.FileKey, file.FileName, expiry, ct);
        return new DownloadTicket(url, expiry);
    }

    private Guid Owner() => currentUser.IsAuthenticated && currentUser.UserId is { } id && id != Guid.Empty
        ? id : throw new FileFailure(401, "A valid authenticated subject is required.");

    private async Task RequireActiveAvatarOwnerAsync(Guid owner, CancellationToken ct)
    {
        if (accounts is null || !await accounts.IsActiveInRoleAsync(owner, currentUser.Role, ct))
            throw new FileFailure(403, "An active account with the current role is required.");
    }

    private async Task<StoredFile> FindAsync(Guid fileId, Guid owner, CancellationToken ct) =>
        await store.FindOwnedAsync(fileId, owner, ct) ?? throw new FileFailure(404, "File not found.");

    private void CheckExpiry(StoredFile file)
    {
        if (timeProvider.GetUtcNow() >= file.ExpiresAt) throw new FileFailure(409, "Upload ticket has expired. Request a new ticket.");
    }

    private static void CheckObject(StoredFile file, ObjectInfo info)
    {
        if (info.SizeBytes != file.SizeBytes || !string.Equals(info.ContentType, file.ContentType, StringComparison.OrdinalIgnoreCase))
            throw new FileFailure(400, "Uploaded object does not match its declared size or content type.");
    }

    private static FileResponse Response(StoredFile file) => new(file.Id, file.FileKey!, file.FileName,
        file.ContentType, file.SizeBytes, file.UploadedAt!.Value);

    private async Task CleanupUnreferencedAsync(string key)
    {
        // Only used before persistence starts or after a definitive CAS rejection. Unknown
        // completion outcomes retain their candidate without relying on an MVCC snapshot.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            if (!await store.IsReferencedAsync(key, timeout.Token)) await storage.DeleteAsync(key, timeout.Token);
        }
        catch (Exception)
        {
            // Infrastructure logs failures. Preserve the original operation/cancellation; retain an
            // orphan rather than risk deleting an accepted object when the DB cannot be consulted.
        }
    }
}
