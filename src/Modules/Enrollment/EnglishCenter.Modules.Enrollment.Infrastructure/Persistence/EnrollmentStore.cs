using EnglishCenter.Modules.Enrollment.Application.Enrollments;
using EnglishCenter.Modules.Enrollment.Domain.Enrollments;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

internal sealed class EnrollmentStore(EnrollmentDbContext db) : IEnrollmentStore
{
    public Task<ClassEnrollment?> FindAsync(Guid id, CancellationToken ct) => db.Enrollments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<ClassEnrollment?> FindAsync(Guid classId, Guid studentId, CancellationToken ct) =>
        db.Enrollments.SingleOrDefaultAsync(x => x.ClassId == classId && x.StudentId == studentId, ct);
    public async Task<IReadOnlyList<ClassEnrollment>> ListAsync(Guid classId, CancellationToken ct) =>
        await db.Enrollments.AsNoTracking().Where(x => x.ClassId == classId).OrderBy(x => x.Id).Take(200).ToListAsync(ct);
    public void Add(ClassEnrollment item) => db.Enrollments.Add(item);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }
            or Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        { throw new EnrollmentFailure(409, "Membership changed concurrently. Reload and retry."); }
    }
}
