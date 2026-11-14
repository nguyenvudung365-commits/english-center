using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Application.PlacementTests;

namespace EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers;

public sealed class PlacementTeacherHandler(IPlacementTestStore store) : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    public Task HandleAsync(UserRegisteredIntegrationEvent e, CancellationToken cancellationToken = default) =>
        e.Role == Roles.Teacher && e.UserId != Guid.Empty ? store.SyncTeacherAsync(e.UserId, cancellationToken) : Task.CompletedTask;
}
