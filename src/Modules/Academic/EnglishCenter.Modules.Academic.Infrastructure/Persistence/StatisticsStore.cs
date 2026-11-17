using EnglishCenter.Modules.Academic.Application.Statistics;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

internal sealed class StatisticsStore(AcademicDbContext db) : IStatisticsStore
{
    // Compare ends in Application: database timestamp parameters cannot preserve the raw clock's last tick.
    public async Task<IReadOnlyList<DateTime>> SessionEndsAsync(Guid classId, CancellationToken ct) =>
        await db.Sessions.AsNoTracking().Where(x => x.ClassId == classId).Select(x => x.EndsAtUtc).ToListAsync(ct);

    public async Task<StatisticsData> ReadAsync(Guid studentId, IReadOnlyCollection<Guid> visibleClassIds,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var sessions = await db.Sessions.AsNoTracking()
            .Where(x => visibleClassIds.Contains(x.ClassId) && x.StartsAtUtc >= fromUtc && x.StartsAtUtc < toUtc)
            .Select(x => new StatisticsSession(x.StartsAtUtc, x.EndsAtUtc)).ToListAsync(ct);
        // Retained own attendance deliberately does not join current membership or restrict to visible classes.
        var attendance = await (from row in db.Attendances.AsNoTracking()
            join session in db.Sessions.AsNoTracking() on row.SessionId equals session.Id
            where row.StudentId == studentId && session.StartsAtUtc >= fromUtc && session.StartsAtUtc < toUtc
            select new { session.StartsAtUtc, row.Status }).ToListAsync(ct);
        return new(sessions, attendance.Select(x => new StatisticsAttendance(x.StartsAtUtc, x.Status.ToString())).ToArray());
    }
}
