using EnglishCenter.Modules.Academic.Domain.Assignments;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

// Kind: "InClass" (Trên lớp) | "Homework" (Về nhà, mặc định). Publish null/true = công bố ngay (giữ hành vi cũ của frontend),
// false = lưu nháp rồi gọi POST /{id}/publish.
public sealed record CreateAssignmentRequest(Guid ClassId, string Title, string? Description, DateTimeOffset OpensAtUtc,
    DateTimeOffset ClosesAtUtc, string? Kind = null, string? Unit = null, string? Lesson = null, Guid? SessionId = null,
    bool? Publish = null);
public sealed record UpdateAssignmentRequest(string Title, string? Description, DateTimeOffset OpensAtUtc, DateTimeOffset ClosesAtUtc,
    string? Kind, string? Unit, string? Lesson, Guid? SessionId, int? ExpectedVersion);
public sealed record PublishAssignmentRequest(int? ExpectedVersion);
public sealed record SubmitAssignmentRequest(Guid[]? FileIds, Guid? ExpectedRevision);
public sealed record GradeSubmissionRequest(decimal? Score, string? Feedback, int? ExpectedGradeVersion);

public sealed record AssignmentResponse(Guid Id, Guid ClassId, string Title, string Description, DateTimeOffset OpensAtUtc,
    DateTimeOffset ClosesAtUtc, string Kind, string? Unit, string? Lesson, Guid? SessionId, bool IsLegacy, string Status,
    DateTimeOffset? PublishedAt, DateTimeOffset? CreatedAt, int Version);
public sealed record SubmissionFileResponse(Guid FileId, string FileName, string ContentType, long SizeBytes);
public sealed record SubmissionResponse(Guid Id, Guid AssignmentId, Guid StudentId, Guid Revision, DateTimeOffset SubmittedAt,
    IReadOnlyList<SubmissionFileResponse> Files, decimal? Score = null, string? Feedback = null, int GradeVersion = 0,
    DateTimeOffset? GradedAt = null, Guid? GradedBy = null);
public sealed record GradeHistoryResponse(int Version, decimal Score, decimal? PreviousScore, string? Feedback, Guid GradedBy,
    string? GradedByName, DateTimeOffset GradedAt);
// Trạng thái của học viên với một bài: NotSubmitted | Submitted | Graded | Missed | Exempted.
public sealed record StudentAssignmentResponse(AssignmentResponse Assignment, string SubmissionStatus, bool IsObligated,
    Guid? SubmissionId, DateTimeOffset? SubmittedAt, decimal? Score, string? Feedback, DateTimeOffset? GradedAt);
public sealed record RecipientResponse(Guid StudentId, DateTimeOffset AssignedAt, string Outcome, string SubmissionStatus,
    Guid? SubmissionId, DateTimeOffset? SubmittedAt, decimal? Score, int GradeVersion);
public sealed record ReplayEventsResponse(int Published, int Graded, int Missed);
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record FileDownloadResponse(Uri Url, DateTimeOffset ExpiresAt);
public sealed record SubmissionLimits(int MaxFiles = 5, long MaxTotalBytes = 50 * 1024 * 1024);

public sealed record AssignmentQuery(string? Search = null, string? Status = null, string? Kind = null);
public sealed record StudentAssignmentQuery(Guid? ClassId = null, string? Search = null, string? Status = null,
    string? SubmissionStatus = null, string? Kind = null, int Page = 1, int PageSize = 20);

public interface IAssignmentStore
{
    Task<Assignment?> FindAssignmentAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Assignment>> AssignmentsAsync(Guid classId, CancellationToken ct);
    Task<IReadOnlyList<Assignment>> AssignmentsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<Assignment>> ClassAssignmentsAsync(IReadOnlyCollection<Guid> classIds, CancellationToken ct);
    Task<Submission?> FindSubmissionAsync(Guid assignmentId, Guid studentId, CancellationToken ct);
    Task<Submission?> FindSubmissionByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Submission>> SubmissionsAsync(Guid assignmentId, CancellationToken ct);
    Task<IReadOnlyList<Submission>> StudentSubmissionsAsync(Guid studentId, IReadOnlyCollection<Guid> assignmentIds, CancellationToken ct);
    Task<IReadOnlyList<SubmissionGrade>> GradeHistoryAsync(Guid submissionId, CancellationToken ct);
    Task<IReadOnlyList<AssignmentRecipient>> RecipientsAsync(Guid assignmentId, CancellationToken ct);
    Task<IReadOnlyList<AssignmentRecipient>> StudentObligationsAsync(Guid studentId, CancellationToken ct);
    // Ứng viên roster từ read model class_students (đồng bộ từ StudentEnrolled); người gọi phải lọc quyền hiện tại.
    Task<IReadOnlyList<Guid>> RosterCandidatesAsync(Guid classId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> StudentClassCandidatesAsync(Guid studentId, CancellationToken ct);
    // Outbox tối giản: các bản ghi đã commit nhưng chưa phát event thành công.
    Task<IReadOnlyList<Assignment>> UnsentPublishedAsync(int max, CancellationToken ct);
    Task<IReadOnlyList<SubmissionGrade>> UnsentGradesAsync(int max, CancellationToken ct);
    Task<IReadOnlyList<AssignmentRecipient>> UnsentMissedAsync(int max, CancellationToken ct);
    // Bài đã công bố (không legacy), chưa chốt nghĩa vụ, có hạn nộp <= dueBy; hạn sớm nhất trước.
    Task<IReadOnlyList<Assignment>> DueUnresolvedAsync(DateTimeOffset dueBy, int max, CancellationToken ct);
    void Add(Assignment item);
    void Add(Submission item);
    void Add(SubmissionGrade item);
    void Add(AssignmentRecipient item);
    Task SaveAsync(CancellationToken ct);
}
