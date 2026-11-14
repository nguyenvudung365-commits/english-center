using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Enrollment.Application.Registrations;

public sealed class EnrollmentFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

// Giáo vụ nhập rõ giới hạn; không tự gán mặc định chưa được xác nhận trong nghiệp vụ.
public sealed record EnrollRequest(Guid StudentId, Guid ClassId, int? MaxAbsences, int? MaxLates, int? MaxSkippedAssignments);
public sealed record EnrollmentResponse(Guid Id, Guid StudentId, Guid ClassId, string Status, DateTime EnrolledAtUtc,
    decimal TuitionFee, int MaxAbsences, int MaxLates, int MaxSkippedAssignments, bool IsActive = true);
public sealed record EnrollmentStatisticsResponse(int CompletedClasses);

public interface IEnrollmentStore
{
    Task AddStudentAsync(Student student, CancellationToken ct);
    Task AddClassAsync(ClassSnapshot value, CancellationToken ct);
    Task<Registration> EnrollAsync(EnrollRequest request, DateTime now, DateOnly today, CancellationToken ct);
    Task<Registration?> FindAsync(Guid id, CancellationToken ct);
    Task<Registration?> FindAsync(Guid classId, Guid studentId, CancellationToken ct);
    Task<IReadOnlyList<Registration>> ListAsync(Guid classId, CancellationToken ct);
    Task<Registration> SetActiveAsync(Guid id, bool active, CancellationToken ct);
    Task<int> CountCompletedClassesAsync(Guid studentId, DateOnly today, CancellationToken ct);
}

public sealed class EnrollmentService(IEnrollmentStore store, IEventBus events, TimeProvider clock,
    ICurrentUser current, IAccountAccess accounts)
{
    internal async Task<Guid> EnsureManagerAsync(CancellationToken ct)
    {
        var userId = await EnsureReaderAsync(ct);
        if (current.Role is not (Roles.Admin or Roles.Staff))
            throw new EnrollmentFailure(403, "Only staff manage enrollment.");
        return userId;
    }

    private async Task<Guid> EnsureReaderAsync(CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is not { } userId || userId == Guid.Empty)
            throw new EnrollmentFailure(401, "Authentication required.");
        if (current.Role is not (Roles.Admin or Roles.Staff or Roles.Student) ||
            !await accounts.IsActiveInRoleAsync(userId, current.Role, ct))
            throw new EnrollmentFailure(403, "An active account with the current role is required.");
        return userId;
    }

    internal async Task<Guid?> StudentFilterAsync(Guid? filter, CancellationToken ct)
    {
        var userId = await EnsureReaderAsync(ct);
        return current.Role == Roles.Student ? userId : filter;
    }

    public async Task<EnrollmentResponse> EnrollAsync(EnrollRequest request, CancellationToken ct)
    {
        await EnsureManagerAsync(ct);
        if (request.StudentId == Guid.Empty || request.ClassId == Guid.Empty || request.MaxAbsences is null or < 0 ||
            request.MaxLates is null or < 0 || request.MaxSkippedAssignments is null or < 0)
            throw new EnrollmentFailure(400, "StudentId, ClassId and three non-negative limits are required.");
        var now = clock.GetUtcNow();
        // PostgreSQL timestamptz lưu microsecond; phản hồi POST và GET phải cùng độ chính xác.
        var enrolledAt = new DateTime(now.UtcDateTime.Ticks / 10 * 10, DateTimeKind.Utc);
        var value = await store.EnrollAsync(request, enrolledAt, LocalDate(now), ct);
        await PublishAsync(value, ct);
        return Response(value);
    }

    public async Task<EnrollmentResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var student = await StudentFilterAsync(null, ct);
        var value = await store.FindAsync(id, ct);
        if (value is null || (student.HasValue && student != value.StudentId)) throw new EnrollmentFailure(404, "Enrollment not found.");
        return Response(value);
    }

    public async Task<EnrollmentStatisticsResponse> GetStatisticsAsync(CancellationToken ct)
    {
        var studentId = await EnsureReaderAsync(ct);
        if (current.Role != Roles.Student)
            throw new EnrollmentFailure(403, "Only students can view their personal statistics.");
        return new(await store.CountCompletedClassesAsync(studentId, LocalDate(clock.GetUtcNow()), ct));
    }

    public async Task ReplayAsync(Guid id, CancellationToken ct)
    {
        await EnsureManagerAsync(ct);
        var value = await store.FindAsync(id, ct) ?? throw new EnrollmentFailure(404, "Enrollment not found.");
        await PublishAsync(value, ct);
    }

    public async Task<EnrollmentResponse> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        await EnsureManagerAsync(ct);
        return Response(await store.SetActiveAsync(id, active, ct));
    }

    private Task PublishAsync(Registration value, CancellationToken ct) => events.PublishAsync(
        new StudentEnrolledIntegrationEvent(value.Id, value.StudentId, value.ClassId, value.TuitionFee, value.EnrolledAt), ct);
    internal static EnrollmentResponse Response(Registration x) => new(x.Id, x.StudentId, x.ClassId, x.Status,
        x.EnrolledAt, x.TuitionFee, x.MaxAbsences, x.MaxLates, x.MaxSkippedAssignments,
        x.Status == "Active" && x.IsActive);
    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(7)).DateTime);
}
