namespace EnglishCenter.Modules.Academic.Domain.Assignments;

// US-18: loại bài "Trên lớp" hoặc "Về nhà".
public enum AssignmentKind
{
    InClass,
    Homework
}

// Trạng thái hiển thị; Draft chỉ người quản lý lớp thấy.
public enum AssignmentStatus
{
    Draft,
    Upcoming,
    Open,
    Closed
}

public sealed class Assignment
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 5000;
    public const int UnitMaxLength = 100;
    public const int LessonMaxLength = 100;

    private Assignment() { }
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset OpensAtUtc { get; private set; }
    public DateTimeOffset ClosesAtUtc { get; private set; }
    public AssignmentKind Kind { get; private set; } = AssignmentKind.Homework;
    public string? Unit { get; private set; }
    public string? Lesson { get; private set; }
    // Buổi học (TV1). Bài legacy tạo trước khi có Session có thể chưa gán.
    public Guid? SessionId { get; private set; }
    // Bài có trước bản nâng cấp: giữ truy cập/nộp theo hạn cũ, không suy diễn nghĩa vụ/missed hồi tố.
    public bool IsLegacy { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }
    // Outbox tối giản: null nghĩa là AssignmentPublished chưa được phát thành công sau commit.
    public DateTimeOffset? PublishedEventSentAt { get; private set; }
    // Thời điểm worker đã chốt nghĩa vụ (Submitted/Missed/Exempted) sau hạn nộp.
    public DateTimeOffset? ObligationsResolvedAt { get; private set; }
    // Token chống ghi đè khi sửa/công bố đồng thời.
    public int Version { get; private set; }

    public bool IsPublished => PublishedAt is not null;
    public bool IsVisibleToStudents => IsLegacy || IsPublished;

    public static Assignment Create(Guid classId, string title, string description, DateTimeOffset opens, DateTimeOffset closes)
        => Create(classId, title, description, opens, closes, AssignmentKind.Homework, null, null, null, Guid.Empty, opens);

    public static Assignment Create(Guid classId, string title, string description, DateTimeOffset opens, DateTimeOffset closes,
        AssignmentKind kind, string? unit, string? lesson, Guid? sessionId, Guid createdBy, DateTimeOffset now)
    {
        if (classId == Guid.Empty) throw new ArgumentException("Invalid assignment.");
        var item = new Assignment
        {
            Id = Guid.NewGuid(), ClassId = classId, CreatedBy = createdBy == Guid.Empty ? null : createdBy,
            CreatedAt = now.ToUniversalTime(), Version = 1
        };
        item.Apply(title, description, opens, closes, kind, unit, lesson, sessionId);
        return item;
    }

    /// <summary>
    /// Bài nháp được sửa mọi trường. Bài đã công bố/legacy chỉ được sửa nội dung mô tả (tiêu đề, mô tả, Unit/Lesson);
    /// đổi loại, buổi hoặc thời hạn sau công bố sẽ làm sai nghĩa vụ đã chốt nên bị từ chối.
    /// </summary>
    public void Update(string title, string description, DateTimeOffset opens, DateTimeOffset closes, AssignmentKind kind,
        string? unit, string? lesson, Guid? sessionId, DateTimeOffset now)
    {
        if (IsVisibleToStudents &&
            (opens.ToUniversalTime() != OpensAtUtc || closes.ToUniversalTime() != ClosesAtUtc || kind != Kind || sessionId != SessionId))
            throw new InvalidOperationException("Published assignments cannot change kind, session or submission window.");
        Apply(title, description, opens, closes, kind, unit, lesson, sessionId);
        Touch(now);
    }

    public void Publish(Guid publishedBy, DateTimeOffset now)
    {
        if (IsLegacy || IsPublished) throw new InvalidOperationException("Assignment is already visible to students.");
        if (publishedBy == Guid.Empty) throw new ArgumentException("Publisher is required.", nameof(publishedBy));
        PublishedAt = now.ToUniversalTime(); PublishedBy = publishedBy;
        Touch(now);
    }

    public void MarkPublishedEventSent(DateTimeOffset now) => PublishedEventSentAt ??= now.ToUniversalTime();

    public void MarkObligationsResolved(DateTimeOffset now)
    {
        if (!IsPublished || IsLegacy) throw new InvalidOperationException("Only published assignments have obligations.");
        ObligationsResolvedAt ??= now.ToUniversalTime();
    }

    public AssignmentStatus StatusAt(DateTimeOffset now) =>
        !IsVisibleToStudents ? AssignmentStatus.Draft
        : now < OpensAtUtc ? AssignmentStatus.Upcoming
        : now < ClosesAtUtc ? AssignmentStatus.Open
        : AssignmentStatus.Closed;

    private void Apply(string title, string description, DateTimeOffset opens, DateTimeOffset closes, AssignmentKind kind,
        string? unit, string? lesson, Guid? sessionId)
    {
        description ??= string.Empty;
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > TitleMaxLength || description.Length > DescriptionMaxLength ||
            opens >= closes || !Enum.IsDefined(kind) || sessionId == Guid.Empty ||
            Clean(unit)?.Length > UnitMaxLength || Clean(lesson)?.Length > LessonMaxLength)
            throw new ArgumentException("Invalid assignment.");
        Title = title.Trim(); Description = description.Trim();
        OpensAtUtc = opens.ToUniversalTime(); ClosesAtUtc = closes.ToUniversalTime();
        Kind = kind; Unit = Clean(unit); Lesson = Clean(lesson); SessionId = sessionId;
    }

    private void Touch(DateTimeOffset now) { UpdatedAt = now.ToUniversalTime(); Version++; }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
