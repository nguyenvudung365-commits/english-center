namespace EnglishCenter.Modules.MediaNotification.Domain.Notifications;

// Chỉ giữ tiêu đề chung và con trỏ (TargetType/TargetId); không chứa điểm, URL ký hay nội dung file.
public sealed class Notification
{
    private Notification() { }

    public Guid Id { get; private set; }
    public Guid RecipientId { get; private set; }
    // Khóa chống trùng: (RecipientId, Kind, SourceId, SourceVersion).
    public string Kind { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public int SourceVersion { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid TargetId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public static Notification Create(Guid recipientId, string kind, Guid sourceId, int sourceVersion,
        string title, string targetType, Guid targetId, DateTime now)
    {
        if (recipientId == Guid.Empty || sourceId == Guid.Empty || targetId == Guid.Empty)
            throw new ArgumentException("Recipient, source and target are required.");
        return new Notification
        {
            Id = Guid.NewGuid(), RecipientId = recipientId, Kind = kind, SourceId = sourceId,
            SourceVersion = sourceVersion, Title = title, TargetType = targetType, TargetId = targetId, CreatedAt = now
        };
    }
}
