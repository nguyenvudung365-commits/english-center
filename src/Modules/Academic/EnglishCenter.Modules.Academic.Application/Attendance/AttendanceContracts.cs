using EnglishCenter.Modules.Academic.Domain.Attendance;

namespace EnglishCenter.Modules.Academic.Application.Attendance;

public sealed record PageQuery(int Page = 1, int PageSize = 20)
{
    public void Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100) throw new AcademicFailure(400, "Page must be positive and pageSize must be 1–100.");
    }
}
public sealed record Page<T>(IReadOnlyList<T> Items, [property: System.Text.Json.Serialization.JsonPropertyName("page")] int PageNumber, int PageSize, int Total);
public sealed record AttendanceQuery(Guid? ClassId = null, Guid? SessionId = null, Guid? StudentId = null,
    string? Status = null, int Page = 1, int PageSize = 20);
public sealed record SummaryQuery(Guid? ClassId = null, Guid? SessionId = null, Guid? StudentId = null, string? Status = null);
public sealed record RecordAttendanceRequest(string? Status, int? ExpectedRevision);
public sealed record ReplayAttendanceRequest(int? Revision, string? Reason);
public sealed record AttendanceRow(Guid Id, Guid SessionId, Guid ClassId, Guid StudentId, string Status, int Revision,
    Guid RecordedBy, DateTimeOffset RecordedAtUtc, Guid UpdatedBy, DateTimeOffset UpdatedAtUtc);
public sealed record AttendanceRevisionResponse(Guid AttendanceId, int Revision, string? PreviousStatus, string Status,
    Guid ChangedBy, DateTimeOffset ChangedAtUtc);
public sealed record StatusCounts(int Present, int AbsentUnexcused, int AbsentExcused, int Late, int LateOver20Min, int LeftEarly);
public sealed record StatusPercents(int Present, int AbsentUnexcused, int AbsentExcused, int Late, int LateOver20Min, int LeftEarly);
public sealed record Summary(int RecordedCount, StatusCounts Counts, StatusPercents Percentages,
    int AbsentCount, int LateCount, int LeftEarlyCount)
{
    public static Summary FromCounts(StatusCounts counts)
    {
        var total = counts.Present + counts.AbsentUnexcused + counts.AbsentExcused + counts.Late + counts.LateOver20Min + counts.LeftEarly;
        int Percent(int count) => total == 0 ? 0 : (int)Math.Round(count * 100m / total, MidpointRounding.AwayFromZero);
        return new(total, counts, new(Percent(counts.Present), Percent(counts.AbsentUnexcused), Percent(counts.AbsentExcused),
            Percent(counts.Late), Percent(counts.LateOver20Min), Percent(counts.LeftEarly)),
            counts.AbsentUnexcused + counts.AbsentExcused, counts.Late + counts.LateOver20Min, counts.LeftEarly);
    }

    public static Summary FromStatuses(IEnumerable<AttendanceStatus> statuses)
    {
        var counts = statuses.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        return FromCounts(new(counts.GetValueOrDefault(AttendanceStatus.Present), counts.GetValueOrDefault(AttendanceStatus.AbsentUnexcused),
            counts.GetValueOrDefault(AttendanceStatus.AbsentExcused), counts.GetValueOrDefault(AttendanceStatus.Late),
            counts.GetValueOrDefault(AttendanceStatus.LateOver20Min), counts.GetValueOrDefault(AttendanceStatus.LeftEarly)));
    }
}
public sealed record RosterItem(Guid StudentId, string? FullName, string? StudentCode, bool IsActive, AttendanceRow? Attendance);
public sealed record RosterPage(Guid SessionId, Guid ClassId, DateTimeOffset AsOfUtc, bool CanWrite, DateTimeOffset WriteClosesAtUtc,
    IReadOnlyList<RosterItem> Items, int Page, int PageSize, int Total);
public sealed record ReplayAttempt(Guid Id, Guid AttendanceId, int Revision, Guid RequestedBy, DateTimeOffset RequestedAtUtc,
    string Reason, string DispatchStatus, DateTimeOffset? CompletedAtUtc);
public sealed record ReplayDispatchResponse(Guid AttemptId, Guid AttendanceId, int Revision, string DispatchStatus);
