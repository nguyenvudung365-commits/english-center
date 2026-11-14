namespace EnglishCenter.Modules.Enrollment.Domain.Registrations;

public sealed class AttendanceSnapshot
{
    public Guid SessionId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public string Status { get; private set; } = "";
    public DateTime RecordedAt { get; private set; }
    private AttendanceSnapshot() { }
    public AttendanceSnapshot(Guid session, Guid student, Guid enrollment, string status, DateTime at)
    { SessionId = session; StudentId = student; EnrollmentId = enrollment; Set(status, at); }
    public void Set(string status, DateTime at) { Status = status; RecordedAt = at; }
}

// Một bài chỉ được Academic kết luận là bỏ sau khi hết hạn nộp/nộp lại.
public sealed class MissedAssignment
{
    public Guid AssignmentId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public DateTime RecordedAt { get; private set; }
    private MissedAssignment() { }
    public MissedAssignment(Guid assignment, Guid enrollment, DateTime at)
    { AssignmentId = assignment; EnrollmentId = enrollment; RecordedAt = at; }
}

public sealed class Warning
{
    public Guid Id { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public string WarningType { get; private set; } = "";
    public string Message { get; private set; } = "";
    public DateTime IssuedAt { get; private set; }
    private Warning() { }
    public Warning(Guid enrollment, string type, string message, DateTime at)
    { Id = Guid.NewGuid(); EnrollmentId = enrollment; WarningType = type; Message = message; IssuedAt = at; }
}
