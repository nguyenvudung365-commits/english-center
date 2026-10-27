using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

public sealed record AssignmentPublishedIntegrationEvent(Guid AssignmentId, Guid ClassId, string Title, DateTime OpensAtUtc, DateTime ClosesAtUtc) : IntegrationEvent;
