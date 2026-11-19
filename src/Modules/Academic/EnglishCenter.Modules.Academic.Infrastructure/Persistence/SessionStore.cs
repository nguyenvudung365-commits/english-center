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
            var item = await LockScheduleAsync(session.ClassId, teacherId, ct);
            await ValidateAsync(session, item, session.Id, ct);
            db.Sessions.Add(session);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (IsLockContention(ex))
        {
            throw new AcademicFailure(409, "Schedule is being changed concurrently. Reload before trying again.");
        }
    }

    public async Task<Session> ChangeAsync(Guid id, Guid classId, Guid teacherId, int expectedRevision, Session? replacement, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var item = await LockScheduleAsync(classId, teacherId, ct);
            var live = await db.Sessions.SingleOrDefaultAsync(x => x.Id == id && x.ClassId == classId, ct)
                ?? throw new AcademicFailure(404, "Session not found.");
            if (live.IsCancelled || live.Revision != expectedRevision)
                throw new AcademicFailure(409, "Session changed or was cancelled. Reload before trying again.");
            if (replacement is null) live.Cancel();
            else
            {
                if ((live.StartsAtUtc != replacement.StartsAtUtc || live.EndsAtUtc != replacement.EndsAtUtc) &&
                    await db.Attendances.AnyAsync(x => x.SessionId == id, ct))
                    throw new AcademicFailure(409, "A session with recorded attendance cannot be rescheduled.");
                await ValidateAsync(replacement, item, id, ct);
                live.ApplySchedule(replacement);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return live;
        }
        catch (Exception ex) when (IsLockContention(ex))
        {
            throw new AcademicFailure(409, "Schedule is being changed concurrently. Reload before trying again.");
        }
    }

    private async Task<Domain.Classes.Class> LockScheduleAsync(Guid classId, Guid teacherId, CancellationToken ct)
    {
        // Lock order is teacher then class; attendance writers take only the same class lock.
        if (await db.Teachers.Where(x => x.Id == teacherId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.FullName, x => x.FullName), ct) != 1)
            throw new AcademicFailure(400, "Assigned teacher does not exist.");
        if (await db.Classes.Where(x => x.Id == classId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Name, x => x.Name), ct) != 1)
            throw new AcademicFailure(404, "Class not found.");
        var item = await db.Classes.AsNoTracking().SingleAsync(x => x.Id == classId, ct);
        if (item.TeacherId != teacherId) throw new AcademicFailure(409, "Assigned teacher changed. Reload the class.");
        return item;
    }

    private async Task ValidateAsync(Session session, Domain.Classes.Class item, Guid excludeId, CancellationToken ct)
    {
        SessionWindow.Validate(item, session.StartsAtUtc, session.EndsAtUtc);
        var overlap = await (from existing in db.Sessions
            join academicClass in db.Classes on existing.ClassId equals academicClass.Id
            where academicClass.TeacherId == item.TeacherId && existing.Id != excludeId && !existing.IsCancelled &&
                existing.StartsAtUtc < session.EndsAtUtc && session.StartsAtUtc < existing.EndsAtUtc
            select existing.Id).AnyAsync(ct);
        if (overlap) throw new AcademicFailure(409, "Teacher already has an overlapping session.");
    }

    private static bool IsLockContention(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure,
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
        DbUpdateException { InnerException: { } inner } => IsLockContention(inner),
        _ => false
    };
}
