using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class AcademicStore(AcademicDbContext db) : IAcademicStore
{
    public Task<bool> ClassCodeExistsAsync(string normalizedCode, CancellationToken ct) =>
        db.Classes.AnyAsync(x => x.Code == normalizedCode, ct);
    public Task<Class?> FindClassAsync(Guid id, CancellationToken ct) => db.Classes.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Course?> FindCourseAsync(Guid id, CancellationToken ct) => db.Courses.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Course>> CoursesAsync(CancellationToken ct) =>
        await db.Courses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
    public Task<Teacher?> FindTeacherAsync(Guid id, CancellationToken ct) => db.Teachers.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Teacher>> TeachersAsync(CancellationToken ct) =>
        await db.Teachers.AsNoTracking().OrderBy(x => x.FullName).ToListAsync(ct);
    public void AddClass(Class academicClass) => db.Classes.Add(academicClass);
    public void AddTeacher(Teacher teacher) => db.Teachers.Add(teacher);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AcademicFailure(409, "Academic data already exists.");
        }
    }
}
