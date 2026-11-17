using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

namespace EnglishCenter.Modules.Academic.Application.Classes;

public sealed record StudentClassResponse(Guid ClassId, string Code, string Name, DateOnly StartDate, DateOnly EndDate);
public interface IClassRoster
{
    Task AddAsync(Guid enrollmentId, Guid studentId, Guid classId, CancellationToken ct);
    Task<IReadOnlyList<StudentClassResponse>> ListAsync(Guid studentId, CancellationToken ct);
}
public sealed class StudentClasses(ClassAccess access)
{
    public async Task<IReadOnlyList<StudentClassResponse>> ListAsync(Guid studentId, CancellationToken ct)
    {
        await access.RequireStudentAsync(studentId, ct);
        return (await access.VisibleClassesAsync(ct)).OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Id)
            .Select(x => new StudentClassResponse(x.Id, x.Code, x.Name, x.StartDate, x.EndDate)).ToList();
    }
}
public sealed class StudentEnrolledHandler(IClassRoster roster) : IIntegrationEventHandler<StudentEnrolledIntegrationEvent>
{
    public Task HandleAsync(StudentEnrolledIntegrationEvent e, CancellationToken cancellationToken = default) =>
        roster.AddAsync(e.EnrollmentId, e.StudentId, e.ClassId, cancellationToken);
}
