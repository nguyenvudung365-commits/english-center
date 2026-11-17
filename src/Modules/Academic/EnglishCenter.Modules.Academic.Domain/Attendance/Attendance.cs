namespace EnglishCenter.Modules.Academic.Domain.Attendance;

public sealed class Attendance
{
    private Attendance() { }
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public int Revision { get; private set; }
    public Guid RecordedBy { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Attendance Create(Guid sessionId, Guid classId, Guid studentId, AttendanceStatus status, Guid actor, DateTime now)
    {
        if (sessionId == Guid.Empty || classId == Guid.Empty || studentId == Guid.Empty || actor == Guid.Empty)
            throw new ArgumentException("Attendance identifiers must not be empty.");
        Validate(status, actor, now);
        now = Microseconds(now);
        return new() { Id = Guid.NewGuid(), SessionId = sessionId, ClassId = classId, StudentId = studentId,
            Status = status, Revision = 1, RecordedBy = actor, RecordedAtUtc = now, UpdatedBy = actor, UpdatedAtUtc = now };
    }

    public AttendanceRevision? Change(AttendanceStatus status, Guid actor, DateTime now)
    {
        Validate(status, actor, now);
        if (status == Status) return null;
        var previous = Status;
        Status = status;
        Revision = checked(Revision + 1);
        UpdatedBy = actor;
        UpdatedAtUtc = Microseconds(now);
        return AttendanceRevision.Capture(this, previous);
    }

    private static void Validate(AttendanceStatus status, Guid actor, DateTime now)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("Unknown attendance status.", nameof(status));
        if (actor == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actor));
        if (now == default || now.Kind != DateTimeKind.Utc) throw new ArgumentException("A UTC time is required.", nameof(now));
    }

    public static DateTime Microseconds(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
