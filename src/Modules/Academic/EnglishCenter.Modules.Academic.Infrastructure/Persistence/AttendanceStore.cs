using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Attendance;
using EnglishCenter.Modules.Academic.Domain.Attendance;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AttendanceEntity = EnglishCenter.Modules.Academic.Domain.Attendance.Attendance;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class AttendanceStore(AcademicDbContext db) : IAttendanceStore
{
    public Task<AttendanceEntity?> FindAsync(Guid id, CancellationToken ct) =>
        db.Attendances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<AttendanceEntity>> ForSessionAsync(Guid sessionId, CancellationToken ct) =>
        await db.Attendances.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.StudentId).ToListAsync(ct);

    public async Task<Page<AttendanceEntity>> ListAsync(AttendanceFilter filter, PageQuery page, CancellationToken ct)
    {
        var query = from row in Filter(filter)
                    join session in db.Sessions on row.SessionId equals session.Id
                    orderby session.StartsAtUtc descending, row.StudentId, row.Id
                    select row;
        return await PageAsync(query, page, ct);
    }

    public async Task<Summary> SummaryAsync(AttendanceFilter filter, CancellationToken ct)
    {
        var counts = await Filter(filter).GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        return Summary.FromCounts(new(counts.GetValueOrDefault(AttendanceStatus.Present), counts.GetValueOrDefault(AttendanceStatus.AbsentUnexcused),
            counts.GetValueOrDefault(AttendanceStatus.AbsentExcused), counts.GetValueOrDefault(AttendanceStatus.Late),
            counts.GetValueOrDefault(AttendanceStatus.LateOver20Min), counts.GetValueOrDefault(AttendanceStatus.LeftEarly)));
    }

    private IQueryable<AttendanceEntity> Filter(AttendanceFilter filter)
    {
        var query = db.Attendances.AsNoTracking();
        if (filter.ClassId is { } classId) query = query.Where(x => x.ClassId == classId);
        if (filter.SessionId is { } sessionId) query = query.Where(x => x.SessionId == sessionId);
        if (filter.StudentId is { } studentId) query = query.Where(x => x.StudentId == studentId);
        if (filter.Status is { } status) query = query.Where(x => x.Status == status);
        return query;
    }

    public Task<Page<AttendanceRevision>> HistoryAsync(Guid attendanceId, PageQuery page, CancellationToken ct) =>
        PageAsync(db.AttendanceRevisions.AsNoTracking().Where(x => x.AttendanceId == attendanceId).OrderByDescending(x => x.Revision), page, ct);

    public Task<AttendanceRevision?> SourceAsync(Guid attendanceId, int revision, CancellationToken ct) =>
        db.AttendanceRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.AttendanceId == attendanceId && x.Revision == revision, ct);

    public async Task<AttendanceWriteResult> WriteAsync(Guid sessionId, Guid studentId, AttendanceStatus status, int? expectedRevision,
        Guid actor, DateTime rawNow, Func<Session, Guid, CancellationToken, Task> recheck, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct)
                ?? throw new AcademicFailure(404, "Session not found.");
            // Future teacher-assignment writers must take this same class lock before changing TeacherId.
            var locked = await db.Classes.Where(x => x.Id == session.ClassId)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Name, x => x.Name), ct);
            if (locked != 1) throw new AcademicFailure(404, "Class not found.");
            await CheckAsync();
            var row = await db.Attendances.SingleOrDefaultAsync(x => x.SessionId == sessionId && x.StudentId == studentId, ct);
            AttendanceRevision? source;
            if (row is null)
            {
                if (expectedRevision is not null) throw Conflict();
                row = AttendanceEntity.Create(sessionId, session.ClassId, studentId, status, actor, rawNow);
                source = AttendanceRevision.Capture(row, null);
                db.Attendances.Add(row);
            }
            else
            {
                if (expectedRevision != row.Revision) throw Conflict();
                source = row.Change(status, actor, rawNow);
            }
            if (source is not null) db.AttendanceRevisions.Add(source);
            await db.SaveChangesAsync(ct);
            await CheckAsync();
            await transaction.CommitAsync(ct);
            return new(row, source);

            async Task CheckAsync()
            {
                var liveSession = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct)
                    ?? throw new AcademicFailure(404, "Session not found.");
                var item = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == liveSession.ClassId, ct)
                    ?? throw new AcademicFailure(404, "Class not found.");
                await recheck(liveSession, item.TeacherId, ct);
            }
        }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw Conflict(); }
        catch (Exception ex) when (IsConflict(ex)) { db.ChangeTracker.Clear(); throw Conflict(); }
    }

    public async Task AddReplayAsync(AttendanceReplayAttempt attempt, CancellationToken ct)
    {
        db.AttendanceReplayAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteReplayAsync(AttendanceReplayAttempt attempt, CancellationToken ct) => await db.SaveChangesAsync(ct);

    public Task<Page<AttendanceReplayAttempt>> ReplaysAsync(Guid attendanceId, PageQuery page, CancellationToken ct) =>
        PageAsync(db.AttendanceReplayAttempts.AsNoTracking().Where(x => x.AttendanceId == attendanceId)
            .OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id), page, ct);

    private static async Task<Page<T>> PageAsync<T>(IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        page.Validate();
        var total = await query.CountAsync(ct);
        var offset = (long)(page.Page - 1) * page.PageSize;
        IReadOnlyList<T> items = offset >= total ? [] : await query.Skip((int)offset).Take(page.PageSize).ToListAsync(ct);
        return new(items, page.Page, page.PageSize, total);
    }

    private static AcademicFailure Conflict() => new(409, "Attendance changed concurrently. Reload before trying again.");
    private static bool IsConflict(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure,
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6 || sqlite.SqliteExtendedErrorCode is 1555 or 2067,
        DbUpdateException { InnerException: { } inner } => IsConflict(inner),
        _ => false
    };
}
