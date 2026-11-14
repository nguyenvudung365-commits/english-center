using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Practice;
using EnglishCenter.Modules.Academic.Domain.Practice;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class PracticeStore(AcademicDbContext db) : IPracticeStore
{
    public Task<PracticeExercise?> FindAsync(Guid id, CancellationToken ct) => db.PracticeExercises.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<PracticeExercise>> ListAsync(Guid classId, CancellationToken ct) =>
        await db.PracticeExercises.AsNoTracking().Where(x => x.ClassId == classId && x.DeletedAt == null).Take(500).ToListAsync(ct);
    public void Add(PracticeExercise item) => db.PracticeExercises.Add(item);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AcademicFailure(409, "Practice exercise changed concurrently. Reload and retry."); }
    }
}
