namespace EnglishCenter.Modules.MediaNotification.Domain.Feedback;

public static class FeedbackStatus
{
    public const string New = "New";
    public const string InProgress = "InProgress";
    public const string Resolved = "Resolved";

    public static bool IsValid(string? status) => status is New or InProgress or Resolved;

    // Mới → Đang xử lý → Đã xử lý; không mở lại, không nhảy cóc.
    public static bool CanMove(string from, string to) => (from, to) is (New, InProgress) or (InProgress, Resolved);
}

public sealed class StudentFeedback
{
    private StudentFeedback() { }

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public int Rating { get; private set; }
    public string Status { get; private set; } = FeedbackStatus.New;
    public DateTime CreatedAt { get; private set; }
    public Guid? HandledBy { get; private set; }
    public DateTime? HandledAt { get; private set; }
    public Guid Revision { get; private set; }

    public static StudentFeedback Create(Guid studentId, string title, string content, int rating, DateTime now) => new()
    {
        Id = Guid.NewGuid(), StudentId = studentId, Title = title, Content = content, Rating = rating,
        CreatedAt = now, Revision = Guid.NewGuid()
    };

    public void MoveTo(string status, Guid actor, DateTime now)
    {
        if (!FeedbackStatus.CanMove(Status, status)) throw new InvalidOperationException("Invalid feedback status transition.");
        Status = status;
        HandledBy = actor;
        HandledAt = now;
        Revision = Guid.NewGuid();
    }
}

