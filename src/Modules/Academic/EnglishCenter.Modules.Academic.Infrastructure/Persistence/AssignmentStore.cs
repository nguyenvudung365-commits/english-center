using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class AssignmentStore(AcademicDbContext db) : IAssignmentStore
{
    public Task<Assignment?> FindAssignmentAsync(Guid id, CancellationToken ct) => db.Assignments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Assignment>> AssignmentsAsync(Guid classId, CancellationToken ct) =>
        await db.Assignments.AsNoTracking().Where(x => x.ClassId == classId).OrderBy(x => x.Title).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
    public Task<Submission?> FindSubmissionAsync(Guid assignmentId, Guid studentId, CancellationToken ct) =>
        db.Submissions.Include(x => x.Files).SingleOrDefaultAsync(x => x.AssignmentId == assignmentId && x.StudentId == studentId, ct);
    public Task<Submission?> FindSubmissionByIdAsync(Guid id, CancellationToken ct) =>
        db.Submissions.Include(x => x.Files).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Submission>> SubmissionsAsync(Guid assignmentId, CancellationToken ct) =>
        await db.Submissions.AsNoTracking().Include(x => x.Files).Where(x => x.AssignmentId == assignmentId).OrderBy(x => x.Id).Take(200).ToListAsync(ct);
    public void Add(Assignment item) => db.Assignments.Add(item);
    public void Add(Submission item) => db.Submissions.Add(item);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AcademicFailure(409, "Submission changed concurrently. Reload and retry."); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }
            or Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
        { throw new AcademicFailure(409, "Submission already exists or changed concurrently. Reload and retry."); }
    }
}
