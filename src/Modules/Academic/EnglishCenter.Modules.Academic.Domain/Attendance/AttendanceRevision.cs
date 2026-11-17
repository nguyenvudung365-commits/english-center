namespace EnglishCenter.Modules.Academic.Domain.Attendance;

// The revision is also the immutable source used to reconstruct a replay event.
public sealed class AttendanceRevision
{
    private AttendanceRevision() { }
    public Guid AttendanceId { get; private set; }
    public int Revision { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public AttendanceStatus? PreviousStatus { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public Guid ChangedBy { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    public static AttendanceRevision Capture(Attendance row, AttendanceStatus? previousStatus) => new()
    {
        AttendanceId = row.Id, Revision = row.Revision, SessionId = row.SessionId, ClassId = row.ClassId,
        StudentId = row.StudentId, PreviousStatus = previousStatus, Status = row.Status,
        ChangedBy = row.UpdatedBy, ChangedAtUtc = row.UpdatedAtUtc
    };
}
