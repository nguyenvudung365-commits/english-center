using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Domain.PlacementTests;

namespace EnglishCenter.Modules.Enrollment.Application.PlacementTests;

public sealed record PlacementTestRequest(Guid StudentId, string? Location, string? Mode, DateTimeOffset? ScheduledAt,
    string? TestName, Guid? TeacherId);
public sealed record UpdatePlacementTestRequest(string? Location, string? Mode, DateTimeOffset? ScheduledAt,
    string? TestName, string? Status, Guid? TeacherId);
public sealed record PlacementTestResponse(Guid Id, Guid StudentId, string Location, string Mode, DateTime ScheduledAtUtc,
    string TestName, string Status, Guid? TeacherId, Guid CreatedBy, DateTime CreatedAtUtc);

public interface IPlacementTestStore
{
    Task SyncTeacherAsync(Guid id, CancellationToken ct);
    Task<bool> StudentExistsAsync(Guid id, CancellationToken ct);
    Task<bool> TeacherExistsAsync(Guid id, CancellationToken ct);
    Task SaveAsync(PlacementTest value, bool create, CancellationToken ct);
    Task<PlacementTest?> FindAsync(Guid id, CancellationToken ct);
    Task<PageResult<PlacementTestResponse>> ListAsync(Guid? student, int page, int size, CancellationToken ct);
}

public sealed class PlacementTestService(IPlacementTestStore store, EnrollmentService access, TimeProvider clock)
{
    public async Task<PlacementTestResponse> CreateAsync(PlacementTestRequest request, CancellationToken ct)
    {
        var creator = await access.EnsureManagerAsync(ct);
        if (!await store.StudentExistsAsync(request.StudentId, ct)) throw new EnrollmentFailure(400, "Student does not exist or is not synchronized yet.");
        await ValidateAsync(request.Location, request.Mode, request.ScheduledAt, request.TestName, "Pending", request.TeacherId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var value = new PlacementTest(request.StudentId, creator, new(now.Ticks / 10 * 10, DateTimeKind.Utc));
        Apply(value, request.Location!, request.Mode!, request.ScheduledAt!.Value, request.TestName!, "Pending", request.TeacherId);
        await store.SaveAsync(value, true, ct);
        return Response(value);
    }
    public async Task<PlacementTestResponse> UpdateAsync(Guid id, UpdatePlacementTestRequest request, CancellationToken ct)
    {
        await access.EnsureManagerAsync(ct);
        var value = await store.FindAsync(id, ct) ?? throw new EnrollmentFailure(404, "Placement test not found.");
        await ValidateAsync(request.Location, request.Mode, request.ScheduledAt, request.TestName, request.Status, request.TeacherId, ct);
        Apply(value, request.Location!, request.Mode!, request.ScheduledAt!.Value, request.TestName!, request.Status!, request.TeacherId);
        await store.SaveAsync(value, false, ct);
        return Response(value);
    }
    public async Task<PlacementTestResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var student = await access.StudentFilterAsync(null, ct);
        var value = await store.FindAsync(id, ct);
        if (value is null || (student.HasValue && student != value.StudentId)) throw new EnrollmentFailure(404, "Placement test not found.");
        return Response(value);
    }
    public async Task<PageResult<PlacementTestResponse>> ListAsync(Guid? filter, int page, int size, CancellationToken ct)
    {
        var student = await access.StudentFilterAsync(filter, ct);
        (page, size) = MonitoringService.Pagination(page, size);
        return await store.ListAsync(student, page, size, ct);
    }
    private async Task ValidateAsync(string? location, string? mode, DateTimeOffset? at, string? name, string? status, Guid? teacher, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(location) || location.Trim().Length > 200 || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 ||
            mode is not ("Offline" or "Online") || status is not ("Pending" or "Done" or "Cancelled") || at is null ||
            (status == "Pending" && at <= clock.GetUtcNow()))
            throw new EnrollmentFailure(400, "Valid location, test name, mode, status and schedule are required; Pending tests must be in the future.");
        if (teacher.HasValue && !await store.TeacherExistsAsync(teacher.Value, ct))
            throw new EnrollmentFailure(400, "Teacher does not exist or is not synchronized yet.");
    }
    private static void Apply(PlacementTest value, string location, string mode, DateTimeOffset at, string name, string status, Guid? teacher)
    {
        var utc = at.UtcDateTime;
        value.Update(location.Trim(), mode, new(utc.Ticks / 10 * 10, DateTimeKind.Utc), name.Trim(), status, teacher);
    }
    public static PlacementTestResponse Response(PlacementTest x) => new(x.Id, x.StudentId, x.Location, x.Mode, x.ScheduledAt,
        x.TestName, x.Status, x.TeacherId, x.CreatedBy, x.CreatedAt);
}
