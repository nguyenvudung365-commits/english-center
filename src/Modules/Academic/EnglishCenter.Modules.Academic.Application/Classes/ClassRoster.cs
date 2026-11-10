using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

namespace EnglishCenter.Modules.Academic.Application.Classes;

public sealed record StudentClassResponse(Guid ClassId, string Code, string Name, DateOnly StartDate, DateOnly EndDate);
public interface IClassRoster
{
    Task AddAsync(Guid enrollmentId, Guid studentId, Guid classId, CancellationToken ct);
    Task<IReadOnlyList<StudentClassResponse>> ListAsync(Guid studentId, CancellationToken ct);
}
public sealed class StudentClasses(IClassRoster roster)
{
    public Task<IReadOnlyList<StudentClassResponse>> ListAsync(Guid studentId, CancellationToken ct) => roster.ListAsync(studentId, ct);
}
public sealed class StudentEnrolledHandler(IClassRoster roster) : IIntegrationEventHandler<StudentEnrolledIntegrationEvent>
{
    public Task HandleAsync(StudentEnrolledIntegrationEvent e, CancellationToken cancellationToken = default) =>
        roster.AddAsync(e.EnrollmentId, e.StudentId, e.ClassId, cancellationToken);
}
