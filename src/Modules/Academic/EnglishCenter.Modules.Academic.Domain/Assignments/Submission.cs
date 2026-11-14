namespace EnglishCenter.Modules.Academic.Domain.Assignments;

public sealed class Submission
{
    public const int FeedbackMaxLength = 2000;
    public const decimal MinScore = 0m;
    public const decimal MaxScore = 10m;

    private Submission() { }
    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid Revision { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? GradedAt { get; private set; }
    // Chưa chấm = null, không giả điểm 0.
    public decimal? Score { get; private set; }
    public string? Feedback { get; private set; }
    public Guid? GradedBy { get; private set; }
    // 0 = chưa chấm; mỗi lần chấm/sửa điểm tăng 1 và có một dòng lịch sử cùng số version.
    public int GradeVersion { get; private set; }
    private readonly List<SubmissionFile> files = [];
    public IReadOnlyCollection<SubmissionFile> Files => files.AsReadOnly();

    public static Submission Create(Guid assignmentId, Guid studentId)
    {
        if (assignmentId == Guid.Empty || studentId == Guid.Empty) throw new ArgumentException("Assignment and student required.");
        return new() { Id = Guid.NewGuid(), AssignmentId = assignmentId, StudentId = studentId };
    }
    public void ReplaceFiles(IReadOnlyList<SubmissionFile> replacements, DateTimeOffset now)
    {
        if (GradedAt is not null) throw new InvalidOperationException("Graded work cannot be replaced.");
        if (replacements.Count is < 1 or > 5 || replacements.Any(x => x.SubmissionId != Id) ||
            replacements.Select(x => x.FileId).Distinct().Count() != replacements.Count)
            throw new ArgumentException("One to five distinct attachments required.");
        var ids = replacements.Select(x => x.FileId).ToHashSet();
        files.RemoveAll(x => !ids.Contains(x.FileId));
        foreach (var replacement in replacements)
            if (files.All(x => x.FileId != replacement.FileId)) files.Add(replacement);
        SubmittedAt = now; Revision = Guid.NewGuid();
    }

    public static bool IsValidScore(decimal score) =>
        score >= MinScore && score <= MaxScore && decimal.Truncate(score * 10m) == score * 10m;

    /// <summary>
    /// Chấm lần đầu hoặc sửa điểm. <paramref name="expectedGradeVersion"/> là version người chấm đã thấy (0 nếu chưa chấm);
    /// lệch version nghĩa là có người khác vừa chấm nên phải tải lại. Trả về dòng lịch sử mới để lưu cùng transaction.
    /// </summary>
    public SubmissionGrade Grade(decimal score, string? feedback, Guid gradedBy, int expectedGradeVersion, DateTimeOffset now)
    {
        if (!IsValidScore(score)) throw new ArgumentOutOfRangeException(nameof(score), "Score must be 0–10 in steps of 0.1.");
        var cleaned = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim();
        if (cleaned?.Length > FeedbackMaxLength) throw new ArgumentException("Feedback is too long.", nameof(feedback));
        if (gradedBy == Guid.Empty) throw new ArgumentException("Grader is required.", nameof(gradedBy));
        if (expectedGradeVersion != GradeVersion) throw new GradeConflictException(GradeVersion);
        var previous = Score;
        GradeVersion++;
        Score = score; Feedback = cleaned; GradedBy = gradedBy; GradedAt = now.ToUniversalTime();
        // Đổi revision để một lần thay file đang chạy song song bị từ chối bởi concurrency token.
        Revision = Guid.NewGuid();
        return SubmissionGrade.Create(Id, GradeVersion, score, cleaned, previous, gradedBy, GradedAt.Value);
    }

    /// <summary>Retry cùng nội dung sau khi mất response: coi là đã áp dụng, không tạo thêm lịch sử/event.</summary>
    public bool IsSameGrade(decimal score, string? feedback, Guid gradedBy, int expectedGradeVersion) =>
        GradeVersion == expectedGradeVersion + 1 && Score == score && GradedBy == gradedBy &&
        Feedback == (string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim());
}

public sealed class GradeConflictException(int currentVersion)
    : InvalidOperationException("The grade changed. Reload before grading again.")
{
    public int CurrentVersion { get; } = currentVersion;
}

// US-21: dấu vết từng lần chấm/sửa điểm (người, thời gian, điểm trước/sau, nhận xét, version).
public sealed class SubmissionGrade
{
    private SubmissionGrade() { }
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int Version { get; private set; }
    public decimal Score { get; private set; }
    public decimal? PreviousScore { get; private set; }
    public string? Feedback { get; private set; }
    public Guid GradedBy { get; private set; }
    public DateTimeOffset GradedAt { get; private set; }
    // Outbox tối giản cho SubmissionGraded: null = chưa phát thành công sau commit.
    public DateTimeOffset? EventSentAt { get; private set; }

    internal static SubmissionGrade Create(Guid submissionId, int version, decimal score, string? feedback, decimal? previous,
        Guid gradedBy, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(), SubmissionId = submissionId, Version = version, Score = score, Feedback = feedback,
            PreviousScore = previous, GradedBy = gradedBy, GradedAt = at
        };

    public void MarkEventSent(DateTimeOffset now) => EventSentAt ??= now.ToUniversalTime();
}

public sealed class SubmissionFile
{
    private SubmissionFile() { }
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid FileId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public static SubmissionFile Create(Guid submissionId, Guid fileId, string name, string contentType, long size)
    {
        if (submissionId == Guid.Empty || fileId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 255 ||
            string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || size is <= 0 or > 20 * 1024 * 1024)
            throw new ArgumentException("Invalid submission attachment.");
        return new() { Id = Guid.NewGuid(), SubmissionId = submissionId, FileId = fileId, FileName = name, ContentType = contentType, SizeBytes = size };
    }
}
