namespace EnglishCenter.Modules.Academic.Application.Statistics;

public sealed record ClassProgress(Guid ClassId, DateTimeOffset AsOfUtc, int TotalSessions,
    int CompletedSessions, int RemainingSessions, int CompletionPercent);

public sealed record PersonalStatistics(string Month, string Timezone, DateTimeOffset AsOfUtc, string Availability,
    AcademicStatistics Academic, IReadOnlyList<ContributorSection> Contributors, IReadOnlyList<MonthPoint> Chart);

public sealed record AcademicStatistics(int CurrentClassCount, int CurrentCompletedSessions,
    int PreviousCompletedSessions, decimal? CompletedSessionsChangePercent, Summary Attendance);

public sealed record ContributorSection(string Key, string Availability, string? Reason,
    IReadOnlyList<ContributorMetric>? Metrics);
public sealed record ContributorMetric(string Key, decimal? Current, decimal? Previous, decimal? ChangePercent);
public sealed record MonthPoint(string Month, int CompletedSessions, int RecordedAttendance, StatusCounts Counts);

public sealed record StatusCounts(int Present, int AbsentUnexcused, int AbsentExcused,
    int Late, int LateOver20Min, int LeftEarly);
public sealed record StatusPercents(int Present, int AbsentUnexcused, int AbsentExcused,
    int Late, int LateOver20Min, int LeftEarly);
public sealed record Summary(int RecordedCount, StatusCounts Counts, StatusPercents Percentages,
    int AbsentCount, int LateCount, int LeftEarlyCount);
