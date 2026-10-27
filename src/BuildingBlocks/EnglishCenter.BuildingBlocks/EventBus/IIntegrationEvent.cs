namespace EnglishCenter.BuildingBlocks.EventBus;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}
