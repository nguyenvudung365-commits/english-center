using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Domain.Classes;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class ClassRoster(AcademicDbContext db) : IClassRoster
{
    public async Task AddAsync(Guid enrollmentId, Guid studentId, Guid classId, CancellationToken ct)
    {
        var existing = await db.ClassStudents.AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == enrollmentId, ct);
        if (existing is not null) { Check(existing, studentId, classId); return; }
        db.ClassStudents.Add(new ClassStudent(enrollmentId, studentId, classId));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.ClassStudents.AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == enrollmentId, ct);
            if (existing is null) throw;
            Check(existing, studentId, classId);
        }
    }
    private static void Check(ClassStudent x, Guid studentId, Guid classId)
    {
        if (x.StudentId != studentId || x.ClassId != classId) throw new InvalidOperationException("Conflicting StudentEnrolled membership.");
    }
    public async Task<IReadOnlyList<StudentClassResponse>> ListAsync(Guid studentId, CancellationToken ct) =>
        await (from member in db.ClassStudents.AsNoTracking()
               join c in db.Classes.AsNoTracking() on member.ClassId equals c.Id
               where member.StudentId == studentId
               orderby c.Code
               select new StudentClassResponse(c.Id, c.Code, c.Name, c.StartDate, c.EndDate)).ToListAsync(ct);
}
