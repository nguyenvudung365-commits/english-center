using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;

namespace EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers;

public sealed class AttendanceRecordedHandler(IMonitoringStore store, MonitoringService service, TimeProvider clock)
    : IIntegrationEventHandler<AttendanceRecordedIntegrationEvent>
{
    private static readonly string[] Statuses = ["Present", "AbsentUnexcused", "AbsentExcused", "Late", "LateOver20Min", "LeftEarly"];
    public async Task HandleAsync(AttendanceRecordedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.SessionId == Guid.Empty || e.StudentId == Guid.Empty || e.ClassId == Guid.Empty ||
            !Statuses.Contains(e.Status) || e.RecordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid AttendanceRecorded payload.");
        var result = await store.RecordAttendanceAsync(e.SessionId, e.StudentId, e.ClassId, e.Status,
            e.RecordedAtUtc, clock.GetUtcNow().UtcDateTime, cancellationToken);
        await service.PublishAsync(result, cancellationToken);
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
