using EnglishCenter.Modules.MediaNotification.Application.Files;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

public sealed class FileStore(MediaNotificationDbContext db, ILogger<FileStore> logger) : IFileStore
{
    public async Task AddAsync(StoredFile file, CancellationToken ct)
    {
        try
        {
            db.StoredFiles.Add(file);
            await db.SaveChangesAsync(ct);
            db.Entry(file).State = EntityState.Detached;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist pending file metadata.");
            throw Unavailable();
        }
    }

    public async Task<StoredFile?> FindOwnedAsync(Guid fileId, Guid ownerId, CancellationToken ct)
    {
        try { return await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId && x.OwnerId == ownerId, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load file metadata.");
            throw Unavailable();
        }
    }

    public async Task<bool> TryCompleteAsync(Guid fileId, Guid ownerId, Guid revision, string fileKey,
        DateTimeOffset uploadedAt, CancellationToken ct)
    {
        var file = await FindOwnedAsync(fileId, ownerId, ct);
        if (file is null || file.Revision != revision || file.UploadedAt is not null || uploadedAt >= file.ExpiresAt) return false;
        db.Attach(file);
        file.Complete(fileKey, uploadedAt);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to complete file metadata.");
            throw Unavailable();
        }
        finally { db.Entry(file).State = EntityState.Detached; }
    }

    public async Task<bool> IsReferencedAsync(string fileKey, CancellationToken ct)
    {
        try { return await db.StoredFiles.AsNoTracking().AnyAsync(x => x.FileKey == fileKey, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cannot verify candidate reference; cleanup must retain the object.");
            throw;
        }
    }

    private static FileFailure Unavailable() => new(503, "File metadata is temporarily unavailable.");
}
