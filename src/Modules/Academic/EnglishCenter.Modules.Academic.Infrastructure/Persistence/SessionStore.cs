using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Sessions;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class SessionStore(AcademicDbContext db) : ISessionStore
{
    public Task<Session?> FindAsync(Guid id, CancellationToken ct) =>
        db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Session>> RangeAsync(DateTime fromUtc, DateTime toUtc,
        IReadOnlyCollection<Guid> visibleClassIds, CancellationToken ct)
    {
        SessionWindow.ValidatePrecision(fromUtc, nameof(fromUtc));
        SessionWindow.ValidatePrecision(toUtc, nameof(toUtc));
        return await db.Sessions.AsNoTracking().Where(x => visibleClassIds.Contains(x.ClassId) && x.StartsAtUtc < toUtc && x.EndsAtUtc > fromUtc)
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
    }

    public async Task CreateWithoutTeacherOverlapAsync(Session session, Guid teacherId, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // All future schedule writers must take this lock before reading overlaps; direct SQL writes are unsupported.
            var affected = await db.Teachers.Where(x => x.Id == teacherId)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.FullName, x => x.FullName), ct);
            if (affected != 1) throw new AcademicFailure(400, "Assigned teacher does not exist.");
            var item = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.ClassId, ct)
                ?? throw new AcademicFailure(404, "Class not found.");
            if (item.TeacherId != teacherId) throw new AcademicFailure(409, "Assigned teacher changed. Reload the class.");
            SessionWindow.Validate(item, session.StartsAtUtc, session.EndsAtUtc);
            var overlap = await (from existing in db.Sessions
                join academicClass in db.Classes on existing.ClassId equals academicClass.Id
                where academicClass.TeacherId == teacherId && existing.StartsAtUtc < session.EndsAtUtc && session.StartsAtUtc < existing.EndsAtUtc
                select existing.Id).AnyAsync(ct);
            if (overlap) throw new AcademicFailure(409, "Teacher already has an overlapping session.");
            db.Sessions.Add(session);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (IsLockContention(ex))
        {
            throw new AcademicFailure(409, "Schedule is being changed concurrently. Reload before trying again.");
        }
    }

    private static bool IsLockContention(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure,
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
        DbUpdateException { InnerException: { } inner } => IsLockContention(inner),
        _ => false
    };
}
