using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;

namespace EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers;

public sealed class AttendanceRecordedHandler(IMonitoringStore store, MonitoringService service, TimeProvider clock)
    : IIntegrationEventHandler<AttendanceRecordedIntegrationEvent>, IIntegrationEventHandler<AttendanceRevisionRecordedIntegrationEvent>
{
    private static readonly string[] Statuses = ["Present", "AbsentUnexcused", "AbsentExcused", "Late", "LateOver20Min", "LeftEarly"];
    public Task HandleAsync(AttendanceRecordedIntegrationEvent e, CancellationToken cancellationToken = default) =>
        RecordAsync(e.SessionId, e.StudentId, e.ClassId, e.Status, e.RecordedAtUtc, 0, cancellationToken);

    Task IIntegrationEventHandler<AttendanceRevisionRecordedIntegrationEvent>.HandleAsync(AttendanceRevisionRecordedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (e.AttendanceId == Guid.Empty || e.Revision < 1) throw new ArgumentException("Invalid attendance revision.");
        return RecordAsync(e.SessionId, e.StudentId, e.ClassId, e.Status, e.RecordedAtUtc, e.Revision, cancellationToken);
    }

    private async Task RecordAsync(Guid session, Guid student, Guid classId, string status, DateTime at, int revision, CancellationToken ct)
    {
        if (session == Guid.Empty || student == Guid.Empty || classId == Guid.Empty ||
            !Statuses.Contains(status) || at.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid AttendanceRecorded payload.");
        var result = await store.RecordAttendanceAsync(session, student, classId, status,
            at, revision, clock.GetUtcNow().UtcDateTime, ct);
        await service.PublishAsync(result, ct);
    }
}

public sealed class AssignmentMissedHandler(IMonitoringStore store, MonitoringService service, TimeProvider clock)
    : IIntegrationEventHandler<AssignmentMissedIntegrationEvent>
{
    public async Task HandleAsync(AssignmentMissedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.AssignmentId == Guid.Empty || e.StudentId == Guid.Empty || e.ClassId == Guid.Empty || e.OccurredOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid AssignmentMissed payload.");
        var result = await store.RecordMissedAssignmentAsync(e.AssignmentId, e.StudentId, e.ClassId,
            e.OccurredOnUtc, clock.GetUtcNow().UtcDateTime, cancellationToken);
        await service.PublishAsync(result, cancellationToken);
    }
}
