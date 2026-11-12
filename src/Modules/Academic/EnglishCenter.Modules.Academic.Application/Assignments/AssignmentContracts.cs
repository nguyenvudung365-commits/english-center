using EnglishCenter.Modules.Academic.Domain.Assignments;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

public sealed record CreateAssignmentRequest(Guid ClassId, string Title, string? Description, DateTimeOffset OpensAtUtc, DateTimeOffset ClosesAtUtc);
public sealed record SubmitAssignmentRequest(Guid[]? FileIds, Guid? ExpectedRevision);
public sealed record AssignmentResponse(Guid Id, Guid ClassId, string Title, string Description, DateTimeOffset OpensAtUtc, DateTimeOffset ClosesAtUtc);
public sealed record SubmissionFileResponse(Guid FileId, string FileName, string ContentType, long SizeBytes);
public sealed record SubmissionResponse(Guid Id, Guid AssignmentId, Guid StudentId, Guid Revision, DateTimeOffset SubmittedAt, IReadOnlyList<SubmissionFileResponse> Files);
public sealed record FileDownloadResponse(Uri Url, DateTimeOffset ExpiresAt);
public sealed record SubmissionLimits(int MaxFiles = 5, long MaxTotalBytes = 50 * 1024 * 1024);

public interface IAssignmentStore
{
    Task<Assignment?> FindAssignmentAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Assignment>> AssignmentsAsync(Guid classId, CancellationToken ct);
    Task<Submission?> FindSubmissionAsync(Guid assignmentId, Guid studentId, CancellationToken ct);
    Task<Submission?> FindSubmissionByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Submission>> SubmissionsAsync(Guid assignmentId, CancellationToken ct);
    void Add(Assignment item);
    void Add(Submission item);
    Task SaveAsync(CancellationToken ct);
}
