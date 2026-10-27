using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.BuildingBlocks.EventBus;

/// <summary>
/// Event bus trong tiến trình. Mỗi lần publish dùng một DI scope mới, gọi tuần tự các handler,
/// handler lỗi được log và không chặn handler còn lại. Không bền vững (xem rủi ro Outbox trong kế hoạch).
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(IServiceScopeFactory scopeFactory, ILogger<InMemoryEventBus> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>();

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(integrationEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Handler {Handler} lỗi khi xử lý event {EventType} ({EventId})",
                    handler.GetType().FullName,
                    typeof(TEvent).Name,
                    integrationEvent.EventId);
            }
        }
    }
}
