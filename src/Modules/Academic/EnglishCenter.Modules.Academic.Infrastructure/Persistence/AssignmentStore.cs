using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

// Lọc/sắp xếp theo DateTimeOffset/decimal làm trong bộ nhớ (tập nhỏ theo lớp/học viên) để chạy được cả SQLite trong test.
internal sealed class AssignmentStore(AcademicDbContext db) : IAssignmentStore
{
    private const int ClassLimit = 200;

    public Task<Assignment?> FindAssignmentAsync(Guid id, CancellationToken ct) => db.Assignments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Assignment>> AssignmentsAsync(Guid classId, CancellationToken ct) =>
        await db.Assignments.AsNoTracking().Where(x => x.ClassId == classId).OrderBy(x => x.Title).ThenBy(x => x.Id).Take(ClassLimit).ToListAsync(ct);
    public async Task<IReadOnlyList<Assignment>> AssignmentsByIdAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await db.Assignments.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
    public async Task<IReadOnlyList<Assignment>> ClassAssignmentsAsync(IReadOnlyCollection<Guid> classIds, CancellationToken ct) =>
        classIds.Count == 0 ? [] : await db.Assignments.AsNoTracking().Where(x => classIds.Contains(x.ClassId))
            .OrderBy(x => x.ClassId).ThenBy(x => x.Title).ThenBy(x => x.Id).Take(ClassLimit * 10).ToListAsync(ct);
    public Task<Submission?> FindSubmissionAsync(Guid assignmentId, Guid studentId, CancellationToken ct) =>
        db.Submissions.Include(x => x.Files).SingleOrDefaultAsync(x => x.AssignmentId == assignmentId && x.StudentId == studentId, ct);
    public Task<Submission?> FindSubmissionByIdAsync(Guid id, CancellationToken ct) =>
        db.Submissions.Include(x => x.Files).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Submission>> SubmissionsAsync(Guid assignmentId, CancellationToken ct) =>
        await db.Submissions.AsNoTracking().Include(x => x.Files).Where(x => x.AssignmentId == assignmentId).OrderBy(x => x.Id).Take(ClassLimit).ToListAsync(ct);
    public async Task<IReadOnlyList<Submission>> StudentSubmissionsAsync(Guid studentId, IReadOnlyCollection<Guid> assignmentIds, CancellationToken ct) =>
        assignmentIds.Count == 0 ? [] : await db.Submissions.AsNoTracking().Include(x => x.Files)
            .Where(x => x.StudentId == studentId && assignmentIds.Contains(x.AssignmentId)).ToListAsync(ct);
    public async Task<IReadOnlyList<SubmissionGrade>> GradeHistoryAsync(Guid submissionId, CancellationToken ct) =>
        await db.SubmissionGrades.AsNoTracking().Where(x => x.SubmissionId == submissionId).OrderBy(x => x.Version).ToListAsync(ct);
    // Có tracking: worker cập nhật kết quả nghĩa vụ trên chính các dòng này.
    public async Task<IReadOnlyList<AssignmentRecipient>> RecipientsAsync(Guid assignmentId, CancellationToken ct) =>
        await db.AssignmentRecipients.Where(x => x.AssignmentId == assignmentId).OrderBy(x => x.StudentId).ToListAsync(ct);
    public async Task<IReadOnlyList<AssignmentRecipient>> StudentObligationsAsync(Guid studentId, CancellationToken ct) =>
        await db.AssignmentRecipients.AsNoTracking().Where(x => x.StudentId == studentId).ToListAsync(ct);
    public async Task<IReadOnlyList<Guid>> RosterCandidatesAsync(Guid classId, CancellationToken ct) =>
        await db.ClassStudents.AsNoTracking().Where(x => x.ClassId == classId).Select(x => x.StudentId).Distinct().ToListAsync(ct);
    public async Task<IReadOnlyList<Guid>> StudentClassCandidatesAsync(Guid studentId, CancellationToken ct) =>
        await db.ClassStudents.AsNoTracking().Where(x => x.StudentId == studentId).Select(x => x.ClassId).Distinct().ToListAsync(ct);
    public async Task<IReadOnlyList<Assignment>> UnsentPublishedAsync(int max, CancellationToken ct) =>
        await db.Assignments.Where(x => x.PublishedAt != null && x.PublishedEventSentAt == null).OrderBy(x => x.Id).Take(max).ToListAsync(ct);
    public async Task<IReadOnlyList<SubmissionGrade>> UnsentGradesAsync(int max, CancellationToken ct) =>
        await db.SubmissionGrades.Where(x => x.EventSentAt == null).OrderBy(x => x.SubmissionId).ThenBy(x => x.Version).Take(max).ToListAsync(ct);
    public async Task<IReadOnlyList<AssignmentRecipient>> UnsentMissedAsync(int max, CancellationToken ct) =>
        await db.AssignmentRecipients.Where(x => x.Outcome == ObligationOutcome.Missed && x.MissedEventSentAt == null)
            .OrderBy(x => x.AssignmentId).ThenBy(x => x.StudentId).Take(max).ToListAsync(ct);
    public async Task<IReadOnlyList<Assignment>> DueUnresolvedAsync(DateTimeOffset dueBy, int max, CancellationToken ct)
    {
        // Chọn bài đến hạn trên tập chiếu nhỏ (Id, hạn) để bài chưa đến hạn không chiếm chỗ của bài đã quá hạn.
        var candidates = await db.Assignments.AsNoTracking()
            .Where(x => x.PublishedAt != null && !x.IsLegacy && x.ObligationsResolvedAt == null)
            .Select(x => new { x.Id, x.ClosesAtUtc }).ToListAsync(ct);
        var ids = candidates.Where(x => x.ClosesAtUtc <= dueBy).OrderBy(x => x.ClosesAtUtc).Take(max).Select(x => x.Id).ToList();
        return ids.Count == 0 ? [] : await db.Assignments.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
    }
    public void Add(Assignment item) => db.Assignments.Add(item);
    public void Add(Submission item) => db.Submissions.Add(item);
    public void Add(SubmissionGrade item) => db.SubmissionGrades.Add(item);
    public void Add(AssignmentRecipient item) => db.AssignmentRecipients.Add(item);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AcademicFailure(409, "Coursework changed concurrently. Reload and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }
            or Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
        { throw new AcademicFailure(409, "Coursework already exists or changed concurrently. Reload and retry."); }
    }
}
