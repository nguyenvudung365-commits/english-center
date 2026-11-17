namespace EnglishCenter.Modules.Academic.Application.Statistics;

public interface IStatisticsStore
{
    Task<IReadOnlyList<DateTime>> SessionEndsAsync(Guid classId, CancellationToken ct);
    Task<StatisticsData> ReadAsync(Guid studentId, IReadOnlyCollection<Guid> visibleClassIds,
        DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed record StatisticsSession(DateTime StartsAtUtc, DateTime EndsAtUtc);
public sealed record StatisticsAttendance(DateTime StartsAtUtc, string Status);
public sealed record StatisticsData(IReadOnlyList<StatisticsSession> Sessions,
    IReadOnlyList<StatisticsAttendance> Attendance);
