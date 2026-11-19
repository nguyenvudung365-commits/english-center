namespace EnglishCenter.Modules.Academic.Domain.Assignments;

// Tài liệu độc lập của lớp: một file Media (purpose "material") do giáo viên/nhân viên đăng, thành viên lớp tải về.
public sealed class ClassMaterial
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private ClassMaterial() { }
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    // Logic reference tới Media (không FK chéo schema); UploadedBy là chủ file.
    public Guid FileId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ClassMaterial Create(Guid classId, string title, string? description, Guid fileId, string fileName, string contentType,
        long sizeBytes, Guid uploadedBy, DateTimeOffset now)
    {
        description = description?.Trim() ?? string.Empty;
        if (classId == Guid.Empty || fileId == Guid.Empty || uploadedBy == Guid.Empty || string.IsNullOrWhiteSpace(title) ||
            title.Trim().Length > TitleMaxLength || description.Length > DescriptionMaxLength ||
            string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || string.IsNullOrWhiteSpace(contentType) ||
            contentType.Length > 128 || sizeBytes is <= 0 or > 20 * 1024 * 1024)
            throw new ArgumentException("Invalid class material.");
        return new()
        {
            Id = Guid.NewGuid(), ClassId = classId, Title = title.Trim(), Description = description, FileId = fileId,
            FileName = fileName, ContentType = contentType, SizeBytes = sizeBytes, UploadedBy = uploadedBy, CreatedAt = now.ToUniversalTime()
        };
    }
}
