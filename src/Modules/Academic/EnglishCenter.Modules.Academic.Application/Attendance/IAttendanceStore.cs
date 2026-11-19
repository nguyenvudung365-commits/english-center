using EnglishCenter.Modules.Academic.Domain.Attendance;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using AttendanceEntity = EnglishCenter.Modules.Academic.Domain.Attendance.Attendance;

namespace EnglishCenter.Modules.Academic.Application.Attendance;

public sealed record AttendanceFilter(Guid? ClassId, Guid? SessionId, Guid? StudentId, AttendanceStatus? Status);
public sealed record AttendanceWriteResult(AttendanceEntity Row, AttendanceRevision? Source);

public interface IAttendanceStore
{
    Task<AttendanceEntity?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<AttendanceEntity>> ForSessionAsync(Guid sessionId, CancellationToken ct);
    Task<Page<AttendanceEntity>> ListAsync(AttendanceFilter filter, PageQuery page, CancellationToken ct);
    Task<Summary> SummaryAsync(AttendanceFilter filter, CancellationToken ct);
    Task<Page<AttendanceRevision>> HistoryAsync(Guid attendanceId, PageQuery page, CancellationToken ct);
    Task<AttendanceRevision?> SourceAsync(Guid attendanceId, int revision, CancellationToken ct);
    Task<AttendanceWriteResult> WriteAsync(Guid sessionId, Guid studentId, AttendanceStatus status, int? expectedRevision,
        Guid actor, DateTime rawNow, Func<Session, Guid, CancellationToken, Task> recheck, CancellationToken ct);
    Task AddReplayAsync(AttendanceReplayAttempt attempt, CancellationToken ct);
    Task CompleteReplayAsync(AttendanceReplayAttempt attempt, CancellationToken ct);
    Task<Page<AttendanceReplayAttempt>> ReplaysAsync(Guid attendanceId, PageQuery page, CancellationToken ct);
}
