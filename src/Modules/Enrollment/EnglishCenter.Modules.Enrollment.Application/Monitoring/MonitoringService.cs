using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;

namespace EnglishCenter.Modules.Enrollment.Application.Monitoring;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record LimitsRequest(int? MaxAbsences, int? MaxLates, int? MaxSkippedAssignments);
public sealed record LimitsResponse(Guid EnrollmentId, Guid StudentId, Guid ClassId, int MaxAbsences, int MaxLates,
    int MaxSkippedAssignments, int AbsenceCount, int LateCount, int SkippedAssignmentCount);
public sealed record WarningResponse(Guid Id, Guid EnrollmentId, Guid StudentId, Guid ClassId, string WarningType,
    string Message, DateTime IssuedAtUtc);
public sealed record MonitoringResult(Registration Enrollment, IReadOnlyList<Warning> NewWarnings);

public interface IMonitoringStore
{
    Task<MonitoringResult> SetLimitsAsync(Guid id, LimitsRequest request, DateTime now, CancellationToken ct);
    Task<MonitoringResult> RecordAttendanceAsync(Guid session, Guid student, Guid classId, string status, DateTime at, DateTime now, CancellationToken ct);
    Task<MonitoringResult> RecordMissedAssignmentAsync(Guid assignment, Guid student, Guid classId, DateTime at, DateTime now, CancellationToken ct);
    Task<PageResult<WarningResponse>> WarningsAsync(Guid? student, Guid? enrollment, int page, int size, CancellationToken ct);
    Task<WarningResponse?> FindWarningAsync(Guid id, CancellationToken ct);
}

public sealed class MonitoringService(IMonitoringStore store, IEnrollmentStore enrollments, IEventBus events, TimeProvider clock)
{
    public static (int Page, int Size) Pagination(int page, int size)
    {
        if (page < 1 || page > 1_000_000 || size < 1) throw new EnrollmentFailure(400, "Invalid pagination.");
        return (page, Math.Min(size, 100));
    }

    public async Task<LimitsResponse> GetLimitsAsync(Guid id, Guid? student, CancellationToken ct)
    {
        var x = await enrollments.FindAsync(id, ct);
        if (x is null || (student.HasValue && student != x.StudentId)) throw new EnrollmentFailure(404, "Enrollment not found.");
        return Response(x);
    }
    public async Task<LimitsResponse> SetLimitsAsync(Guid id, LimitsRequest request, CancellationToken ct)
    {
        if (request.MaxAbsences is null or < 0 || request.MaxLates is null or < 0 || request.MaxSkippedAssignments is null or < 0)
            throw new EnrollmentFailure(400, "Three non-negative limits are required.");
        var result = await store.SetLimitsAsync(id, request, clock.GetUtcNow().UtcDateTime, ct);
        await PublishAsync(result, ct);
        return Response(result.Enrollment);
    }
    public Task<PageResult<WarningResponse>> WarningsAsync(Guid? student, Guid? enrollment, int page, int size, CancellationToken ct)
    {
        (page, size) = Pagination(page, size);
        return store.WarningsAsync(student, enrollment, page, size, ct);
    }
    public async Task ReplayAsync(Guid id, CancellationToken ct)
    {
        var x = await store.FindWarningAsync(id, ct) ?? throw new EnrollmentFailure(404, "Warning not found.");
        await PublishWarningAsync(x, ct);
    }
    public async Task PublishAsync(MonitoringResult result, CancellationToken ct)
    {
        foreach (var x in result.NewWarnings)
            await PublishWarningAsync(new(x.Id, x.EnrollmentId, result.Enrollment.StudentId, result.Enrollment.ClassId,
                x.WarningType, x.Message, x.IssuedAt), ct);
    }
    private Task PublishWarningAsync(WarningResponse x, CancellationToken ct) => events.PublishAsync(
        new WarningIssuedIntegrationEvent(x.Id, x.EnrollmentId, x.StudentId, x.ClassId, x.WarningType)
        { EventId = x.Id, OccurredOnUtc = x.IssuedAtUtc }, ct);
    private static LimitsResponse Response(Registration x) => new(x.Id, x.StudentId, x.ClassId, x.MaxAbsences,
        x.MaxLates, x.MaxSkippedAssignments, x.AbsenceCount, x.LateCount, x.SkippedAssignmentCount);
}
