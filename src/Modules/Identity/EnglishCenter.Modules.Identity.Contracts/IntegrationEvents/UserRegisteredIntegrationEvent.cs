using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;

public sealed record UserRegisteredIntegrationEvent(Guid UserId, string Email, string FullName, string Role,
    long ProfileVersion = 0) : IntegrationEvent;
