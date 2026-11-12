namespace EnglishCenter.Modules.MediaNotification.Domain.Files;

public sealed class StoredFile
{
    private StoredFile() { }

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string StagingKey { get; private set; } = string.Empty;
    public string? FileKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UploadedAt { get; private set; }
    public Guid Revision { get; private set; }

    public static StoredFile Create(Guid ownerId, string fileName, string contentType, long sizeBytes,
        string purpose, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        var id = Guid.NewGuid();
        return new StoredFile
        {
            Id = id, OwnerId = ownerId, FileName = fileName, ContentType = contentType,
            SizeBytes = sizeBytes, Purpose = purpose, CreatedAt = now, ExpiresAt = expiresAt,
            StagingKey = $"staging/{ownerId:N}/{id:N}/{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}",
            Revision = Guid.NewGuid()
        };
    }

    public void Complete(string fileKey, DateTimeOffset now)
    {
        if (UploadedAt is not null) throw new InvalidOperationException("File is already uploaded.");
        FileKey = fileKey;
        UploadedAt = now;
        Revision = Guid.NewGuid();
    }
}
