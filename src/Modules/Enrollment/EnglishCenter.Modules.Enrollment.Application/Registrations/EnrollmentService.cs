using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;

namespace EnglishCenter.Modules.Enrollment.Application.Registrations;

public sealed class EnrollmentFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

// Giáo vụ nhập rõ giới hạn; không tự gán mặc định chưa được xác nhận trong nghiệp vụ.
public sealed record EnrollRequest(Guid StudentId, Guid ClassId, int? MaxAbsences, int? MaxLates, int? MaxSkippedAssignments);
public sealed record EnrollmentResponse(Guid Id, Guid StudentId, Guid ClassId, string Status, DateTime EnrolledAtUtc,
    decimal TuitionFee, int MaxAbsences, int MaxLates, int MaxSkippedAssignments);

public interface IEnrollmentStore
{
    Task AddStudentAsync(Student student, CancellationToken ct);
    Task AddClassAsync(ClassSnapshot value, CancellationToken ct);
    Task<Registration> EnrollAsync(EnrollRequest request, DateTime now, DateOnly today, CancellationToken ct);
    Task<Registration?> FindAsync(Guid id, CancellationToken ct);
}

public sealed class EnrollmentService(IEnrollmentStore store, IEventBus events, TimeProvider clock)
{
    public async Task<EnrollmentResponse> EnrollAsync(EnrollRequest request, CancellationToken ct)
    {
        if (request.StudentId == Guid.Empty || request.ClassId == Guid.Empty || request.MaxAbsences is null or < 0 ||
            request.MaxLates is null or < 0 || request.MaxSkippedAssignments is null or < 0)
            throw new EnrollmentFailure(400, "StudentId, ClassId and three non-negative limits are required.");
        var now = clock.GetUtcNow();
        // PostgreSQL timestamptz lưu microsecond; phản hồi POST và GET phải cùng độ chính xác.
        var enrolledAt = new DateTime(now.UtcDateTime.Ticks / 10 * 10, DateTimeKind.Utc);
        var value = await store.EnrollAsync(request, enrolledAt,
            DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(7)).DateTime), ct);
        await PublishAsync(value, ct);
        return Response(value);
    }

    public async Task<EnrollmentResponse> GetAsync(Guid id, Guid? student, CancellationToken ct)
    {
        var value = await store.FindAsync(id, ct);
        if (value is null || (student.HasValue && student != value.StudentId)) throw new EnrollmentFailure(404, "Enrollment not found.");
        return Response(value);
    }

    public async Task ReplayAsync(Guid id, CancellationToken ct)
    {
        var value = await store.FindAsync(id, ct) ?? throw new EnrollmentFailure(404, "Enrollment not found.");
        await PublishAsync(value, ct);
    }

    private Task PublishAsync(Registration value, CancellationToken ct) => events.PublishAsync(
        new StudentEnrolledIntegrationEvent(value.Id, value.StudentId, value.ClassId, value.TuitionFee, value.EnrolledAt), ct);
    private static EnrollmentResponse Response(Registration x) => new(x.Id, x.StudentId, x.ClassId, x.Status,
        x.EnrolledAt, x.TuitionFee, x.MaxAbsences, x.MaxLates, x.MaxSkippedAssignments);
}
