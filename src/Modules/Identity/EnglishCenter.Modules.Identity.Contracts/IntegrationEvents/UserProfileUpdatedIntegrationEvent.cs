using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;

public sealed record UserProfileUpdatedIntegrationEvent(Guid UserId, string Email, string FullName,
    string Role, long ProfileVersion) : IntegrationEvent;
