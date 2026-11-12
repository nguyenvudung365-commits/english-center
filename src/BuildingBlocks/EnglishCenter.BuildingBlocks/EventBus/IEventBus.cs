namespace EnglishCenter.BuildingBlocks.EventBus;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default,
        bool throwOnHandlerFailure = false)
        where TEvent : IIntegrationEvent;
}
