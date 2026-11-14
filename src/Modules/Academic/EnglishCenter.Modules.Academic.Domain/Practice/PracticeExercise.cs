namespace EnglishCenter.Modules.Academic.Domain.Practice;

/// <summary>
/// US-32 / Q-03: bài luyện tập dạng văn bản theo lớp. Chưa có quiz, chấm tự động hay nộp bài.
/// Xóa là xóa mềm để giữ dấu vết người/thời gian.
/// </summary>
public sealed class PracticeExercise
{
    public const int TitleMaxLength = 200;
    public const int ContentMaxLength = 20000;

    private PracticeExercise() { }
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public string CreatedByRole { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public int Version { get; private set; }
    public bool IsDeleted => DeletedAt is not null;

    public static PracticeExercise Create(Guid classId, string title, string content, Guid createdBy, string createdByRole, DateTimeOffset now)
    {
        if (classId == Guid.Empty || createdBy == Guid.Empty || string.IsNullOrWhiteSpace(createdByRole) || createdByRole.Length > 20)
            throw new ArgumentException("Invalid practice exercise.");
        var item = new PracticeExercise
        {
            Id = Guid.NewGuid(), ClassId = classId, CreatedBy = createdBy, CreatedByRole = createdByRole,
            CreatedAt = now.ToUniversalTime(), Version = 1
        };
        item.Apply(title, content);
        return item;
    }

    public void Update(string title, string content, Guid updatedBy, DateTimeOffset now)
    {
        if (IsDeleted) throw new InvalidOperationException("Deleted practice exercises cannot be edited.");
        if (updatedBy == Guid.Empty) throw new ArgumentException("Editor is required.", nameof(updatedBy));
        Apply(title, content);
        UpdatedBy = updatedBy; UpdatedAt = now.ToUniversalTime(); Version++;
    }

    public void Delete(Guid deletedBy, DateTimeOffset now)
    {
        if (IsDeleted) return;
        if (deletedBy == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(deletedBy));
        DeletedBy = deletedBy; DeletedAt = now.ToUniversalTime(); Version++;
    }

    public static bool IsValid(string? title, string? content) =>
        !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= TitleMaxLength &&
        !string.IsNullOrWhiteSpace(content) && content.Trim().Length <= ContentMaxLength;

    private void Apply(string title, string content)
    {
        if (!IsValid(title, content)) throw new ArgumentException("Invalid practice exercise.");
        Title = title.Trim(); Content = content.Trim();
    }
}
