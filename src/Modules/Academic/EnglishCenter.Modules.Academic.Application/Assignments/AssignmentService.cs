using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;
using EnglishCenter.Modules.MediaNotification.Contracts;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

public sealed class AssignmentService(IAssignmentStore store, IAcademicStore classes, IEnrollmentAccess enrollment,
    ISubmissionFileAccess media, ICurrentUser current, TimeProvider clock, SubmissionLimits limits, IAccountAccess accounts)
{
    private Guid UserId() => current.IsAuthenticated && current.UserId is { } id && id != Guid.Empty
        ? id : throw new AcademicFailure(401, "Authentication required.");
    private bool Manager => current.Role is Roles.Admin or Roles.Staff;

    private async Task AuthorizeAsync(Guid classId, bool manage, CancellationToken ct)
    {
        var userId = UserId();
        if (!await accounts.IsActiveInRoleAsync(userId, current.Role, ct))
            throw new AcademicFailure(403, "An active account with the current role is required.");
        var item = await classes.FindClassAsync(classId, ct) ?? throw new AcademicFailure(404, "Class not found.");
        if (Manager || (current.Role == Roles.Teacher && item.TeacherId == userId)) return;
        if (!manage && current.Role == Roles.Student && await enrollment.IsActiveAsync(classId, userId, ct)) return;
        throw new AcademicFailure(403, "You do not have access to this class.");
    }
    private async Task<Assignment> FindAsync(Guid id, CancellationToken ct) =>
        await store.FindAssignmentAsync(id, ct) ?? throw new AcademicFailure(404, "Assignment not found.");

    public async Task<AssignmentResponse> CreateAsync(CreateAssignmentRequest request, CancellationToken ct)
    {
        await AuthorizeAsync(request.ClassId, true, ct);
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 || (request.Description?.Length ?? 0) > 5000 ||
            request.OpensAtUtc == default || request.ClosesAtUtc == default || request.OpensAtUtc >= request.ClosesAtUtc)
            throw new AcademicFailure(400, "A title (maximum 200 characters), valid opening and closing times are required.");
        var item = Assignment.Create(request.ClassId, request.Title, request.Description ?? "", request.OpensAtUtc, request.ClosesAtUtc);
        store.Add(item); await store.SaveAsync(ct); return Response(item);
    }
    public async Task<IReadOnlyList<AssignmentResponse>> ListAsync(Guid classId, CancellationToken ct)
    {
        await AuthorizeAsync(classId, false, ct);
        return (await store.AssignmentsAsync(classId, ct)).Select(Response).ToList();
    }
    public async Task<AssignmentResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await FindAsync(id, ct); await AuthorizeAsync(item.ClassId, false, ct); return Response(item);
    }
    public async Task<SubmissionResponse> SubmitAsync(Guid assignmentId, SubmitAssignmentRequest request, CancellationToken ct)
    {
        var userId = UserId();
        if (current.Role != Roles.Student) throw new AcademicFailure(403, "Only students submit assignments.");
        var assignment = await FindAsync(assignmentId, ct);
        await AuthorizeAsync(assignment.ClassId, false, ct);
        CheckWindow(assignment);
        var ids = request.FileIds;
        if (ids is null || ids.Length < 1 || ids.Length > limits.MaxFiles || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new AcademicFailure(400, $"Select 1–{limits.MaxFiles} distinct, nonempty file IDs.");
        var confirmed = await media.GetConfirmedAsync(userId, ids, ct);
        if (confirmed is null || confirmed.Count != ids.Length || !confirmed.Select(x => x.FileId).ToHashSet().SetEquals(ids) ||
            confirmed.Any(x => x.SizeBytes is <= 0 or > 20 * 1024 * 1024) || confirmed.Sum(x => x.SizeBytes) > limits.MaxTotalBytes)
            throw new AcademicFailure(400, "Files must be confirmed, owned by you, intended for submission and within size limits.");
        var item = await store.FindSubmissionAsync(assignmentId, userId, ct);
        if (item?.GradedAt is not null) throw new AcademicFailure(409, "Graded submissions cannot be replaced.");
        // Set equality makes a retry safe after a lost response, irrespective of attachment ordering.
        if (item is not null && item.Files.Select(x => x.FileId).ToHashSet().SetEquals(ids)) return Response(item);
        if ((item is null && request.ExpectedRevision is not null) || (item is not null && request.ExpectedRevision != item.Revision))
            throw new AcademicFailure(409, "Submission changed. Reload before replacing its files.");
        if (item is null) { item = Submission.Create(assignmentId, userId); store.Add(item); }
        var snapshots = confirmed.Select(x => SubmissionFile.Create(item.Id, x.FileId, x.FileName, x.ContentType, x.SizeBytes)).ToList();
        // A single SaveChanges transaction persists the aggregate plus all attachments (or none).
        CheckWindow(assignment);
        item.ReplaceFiles(snapshots, clock.GetUtcNow());
        await store.SaveAsync(ct); return Response(item);
    }
    private void CheckWindow(Assignment assignment)
    {
        var now = clock.GetUtcNow();
        if (now < assignment.OpensAtUtc || now >= assignment.ClosesAtUtc) throw new AcademicFailure(409, "Assignment is not open for submission.");
    }
    public async Task<SubmissionResponse> MineAsync(Guid assignmentId, CancellationToken ct)
    {
        var id = UserId();
        if (current.Role != Roles.Student) throw new AcademicFailure(403, "Only students access their own submission.");
        var assignment = await FindAsync(assignmentId, ct); await AuthorizeAsync(assignment.ClassId, false, ct);
        return Response(await store.FindSubmissionAsync(assignmentId, id, ct) ?? throw new AcademicFailure(404, "No submission yet."));
    }
    public async Task<IReadOnlyList<SubmissionResponse>> ListSubmissionsAsync(Guid assignmentId, CancellationToken ct)
    {
        var assignment = await FindAsync(assignmentId, ct); await AuthorizeAsync(assignment.ClassId, true, ct);
        return (await store.SubmissionsAsync(assignmentId, ct)).Select(Response).ToList();
    }
    public async Task<FileDownloadResponse> DownloadAsync(Guid submissionId, Guid fileId, CancellationToken ct)
    {
        var id = UserId();
        var item = await store.FindSubmissionByIdAsync(submissionId, ct) ?? throw new AcademicFailure(404, "Submission not found.");
        var assignment = await FindAsync(item.AssignmentId, ct);
        if (current.Role == Roles.Student && item.StudentId != id) throw new AcademicFailure(403, "This is another student's submission.");
        await AuthorizeAsync(assignment.ClassId, current.Role != Roles.Student, ct);
        if (item.Files.All(x => x.FileId != fileId)) throw new AcademicFailure(404, "File is not attached to this submission.");
        var download = await media.DownloadAsync(item.StudentId, fileId, ct) ?? throw new AcademicFailure(404, "Confirmed file not found.");
        return new(download.Url, download.ExpiresAt);
    }
    private static AssignmentResponse Response(Assignment item) => new(item.Id, item.ClassId, item.Title, item.Description, item.OpensAtUtc, item.ClosesAtUtc);
    private static SubmissionResponse Response(Submission item) => new(item.Id, item.AssignmentId, item.StudentId, item.Revision, item.SubmittedAt,
        item.Files.OrderBy(x => x.FileName).ThenBy(x => x.FileId).Select(x => new SubmissionFileResponse(x.FileId, x.FileName, x.ContentType, x.SizeBytes)).ToList());
}
