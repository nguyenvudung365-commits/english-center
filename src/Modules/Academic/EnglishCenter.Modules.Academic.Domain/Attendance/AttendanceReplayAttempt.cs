namespace EnglishCenter.Modules.Academic.Domain.Attendance;

public enum ReplayDispatchStatus { Pending, Dispatched, Failed }

public sealed class AttendanceReplayAttempt
{
    private AttendanceReplayAttempt() { }
    public Guid Id { get; private set; }
    public Guid AttendanceId { get; private set; }
    public int Revision { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public ReplayDispatchStatus DispatchStatus { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static AttendanceReplayAttempt Create(Guid attendanceId, int revision, Guid actor, DateTime now, string reason)
    {
        if (attendanceId == Guid.Empty || revision < 1 || actor == Guid.Empty || now == default || now.Kind != DateTimeKind.Utc ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new ArgumentException("Invalid replay request.");
        return new() { Id = Guid.NewGuid(), AttendanceId = attendanceId, Revision = revision, RequestedBy = actor,
            RequestedAtUtc = Attendance.Microseconds(now), Reason = reason.Trim(), DispatchStatus = ReplayDispatchStatus.Pending };
    }

    public void Complete(bool dispatched, DateTime now)
    {
        if (DispatchStatus != ReplayDispatchStatus.Pending) throw new InvalidOperationException("Replay has already completed.");
        if (now == default || now.Kind != DateTimeKind.Utc) throw new ArgumentException("A UTC completion time is required.", nameof(now));
        DispatchStatus = dispatched ? ReplayDispatchStatus.Dispatched : ReplayDispatchStatus.Failed;
        CompletedAtUtc = Attendance.Microseconds(now);
    }
}
