using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.PlacementTests;
using EnglishCenter.Modules.Enrollment.Domain.PlacementTests;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

internal sealed class PlacementTestStore(EnrollmentDbContext db) : IPlacementTestStore
{
    public async Task SyncTeacherAsync(Guid id, CancellationToken ct)
    {
        if (await TeacherExistsAsync(id, ct)) return;
        db.TestTeachers.Add(new(id));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await TeacherExistsAsync(id, ct)) throw;
        }
    }
    public Task<bool> StudentExistsAsync(Guid id, CancellationToken ct) => db.Students.AnyAsync(x => x.StudentId == id, ct);
    public Task<bool> TeacherExistsAsync(Guid id, CancellationToken ct) => db.TestTeachers.AnyAsync(x => x.TeacherId == id, ct);
    public async Task SaveAsync(PlacementTest value, bool create, CancellationToken ct)
    {
        if (create) db.PlacementTests.Add(value);
        await db.SaveChangesAsync(ct);
    }
    public Task<PlacementTest?> FindAsync(Guid id, CancellationToken ct) => db.PlacementTests.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<PageResult<PlacementTestResponse>> ListAsync(Guid? student, int page, int size, CancellationToken ct)
    {
        var query = db.PlacementTests.AsNoTracking();
        if (student.HasValue) query = query.Where(x => x.StudentId == student);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.ScheduledAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items.Select(PlacementTestService.Response).ToList(), total, page, size);
    }
}
