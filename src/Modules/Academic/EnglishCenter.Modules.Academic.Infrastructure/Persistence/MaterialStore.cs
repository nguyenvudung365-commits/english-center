using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class MaterialStore(AcademicDbContext db) : IMaterialStore
{
    public Task<ClassMaterial?> FindAsync(Guid id, CancellationToken ct) =>
        db.ClassMaterials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    // Sắp xếp theo thời gian trong bộ nhớ (tập nhỏ theo lớp) để chạy được cả SQLite trong test; mới nhất trước.
    public async Task<IReadOnlyList<ClassMaterial>> ListAsync(Guid classId, CancellationToken ct) =>
        (await db.ClassMaterials.AsNoTracking().Where(x => x.ClassId == classId).ToListAsync(ct))
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToList();

    public void Add(ClassMaterial item) => db.ClassMaterials.Add(item);

    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }
            or Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 or 1555 })
        { throw new AcademicFailure(409, "This file is already published as a material."); }
    }
}
