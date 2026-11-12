using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;

namespace EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers;

public sealed class UserRegisteredHandler(IEnrollmentStore store) : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public Task HandleAsync(UserRegisteredIntegrationEvent e, CancellationToken cancellationToken = default) =>
        e.Role == "Student" ? store.AddStudentAsync(new Student(e.UserId, e.FullName, e.Email), cancellationToken) : Task.CompletedTask;
}

public sealed class ClassCreatedHandler(IEnrollmentStore store) : IIntegrationEventHandler<ClassCreatedIntegrationEvent>
{
    public Task HandleAsync(ClassCreatedIntegrationEvent e, CancellationToken cancellationToken = default) =>
        store.AddClassAsync(new ClassSnapshot(e.ClassId, e.ClassCode, e.StartDate, e.EndDate, e.MaxStudents, e.TuitionFee), cancellationToken);
}
