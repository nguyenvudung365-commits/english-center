using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

public sealed record SessionScheduleChangedIntegrationEvent(Guid SessionId, Guid ClassId, Guid TeacherId,
    int Revision, bool IsCancelled) : IntegrationEvent;
