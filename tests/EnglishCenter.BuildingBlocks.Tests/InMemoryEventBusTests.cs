using EnglishCenter.BuildingBlocks.EventBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EnglishCenter.BuildingBlocks.Tests;

public sealed record TestEvent(string Name) : IntegrationEvent;
public sealed record OtherEvent : IntegrationEvent;

public sealed class CallLog
{
    public List<string> Calls { get; } = new();
    public List<Guid> ScopeIds { get; } = new();
    public CancellationToken LastToken { get; set; }
}

public sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class FirstHandler(CallLog log, ScopeMarker marker) : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent e, CancellationToken ct = default)
    {
        log.Calls.Add("first");
        log.ScopeIds.Add(marker.Id);
        log.LastToken = ct;
        return Task.CompletedTask;
    }
}

public sealed class SecondHandler(CallLog log) : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent e, CancellationToken ct = default)
    {
        log.Calls.Add("second");
        return Task.CompletedTask;
    }
}

public sealed class ThrowingHandler(CallLog log) : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent e, CancellationToken ct = default)
    {
        log.Calls.Add("throwing");
        throw new InvalidOperationException("boom");
    }
}

public sealed class CancelingHandler(CallLog log) : IIntegrationEventHandler<TestEvent>
{
    public Task HandleAsync(TestEvent e, CancellationToken ct = default)
    {
        log.Calls.Add("canceling");
        throw new OperationCanceledException(ct);
    }
}

public sealed class OtherHandler(CallLog log) : IIntegrationEventHandler<OtherEvent>
{
    public Task HandleAsync(OtherEvent e, CancellationToken ct = default)
    {
        log.Calls.Add("other");
        return Task.CompletedTask;
    }
}

public class InMemoryEventBusTests
{
    private static (IEventBus Bus, CallLog Log) Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CallLog>();
        services.AddScoped<ScopeMarker>();
        services.AddInMemoryEventBus();
        configure(services);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IEventBus>(), provider.GetRequiredService<CallLog>());
    }

    [Fact]
    public async Task E1_Publish_WithNoHandlers_DoesNotThrow()
    {
        var (bus, _) = Build(_ => { });
        await bus.PublishAsync(new TestEvent("x"));
    }

    [Fact]
    public async Task E2_TwoHandlers_BothCalledOnceInRegistrationOrder()
    {
        var (bus, log) = Build(s =>
        {
            s.AddIntegrationEventHandler<TestEvent, FirstHandler>();
            s.AddIntegrationEventHandler<TestEvent, SecondHandler>();
        });
        await bus.PublishAsync(new TestEvent("x"));
        Assert.Equal(new[] { "first", "second" }, log.Calls);
    }

    [Fact]
    public async Task E3_FirstHandlerThrows_SecondStillCalled_PublishDoesNotThrow()
    {
        var (bus, log) = Build(s =>
        {
            s.AddIntegrationEventHandler<TestEvent, ThrowingHandler>();
            s.AddIntegrationEventHandler<TestEvent, SecondHandler>();
        });
        await bus.PublishAsync(new TestEvent("x"));
        Assert.Equal(new[] { "throwing", "second" }, log.Calls);
    }

    [Fact]
    public async Task E3b_HandlerFailure_IsLoggedAsError()
    {
        var logger = new CapturingLoggerProvider();
        var (bus, _) = Build(s =>
        {
            s.AddLogging(b => b.AddProvider(logger));
            s.AddIntegrationEventHandler<TestEvent, ThrowingHandler>();
        });
        await bus.PublishAsync(new TestEvent("x"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task E4_CanceledToken_AndHandlerThrowsOce_PublishRethrows()
    {
        var (bus, log) = Build(s =>
        {
            s.AddIntegrationEventHandler<TestEvent, CancelingHandler>();
            s.AddIntegrationEventHandler<TestEvent, SecondHandler>();
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bus.PublishAsync(new TestEvent("x"), cts.Token));
        Assert.DoesNotContain("second", log.Calls);
    }

    [Fact]
    public async Task E5_NullEvent_ThrowsArgumentNullException()
    {
        var (bus, _) = Build(_ => { });
        await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync<TestEvent>(null!));
    }

    [Fact]
    public async Task E6_HandlerOfOtherEvent_NotCalled()
    {
        var (bus, log) = Build(s =>
        {
            s.AddIntegrationEventHandler<TestEvent, FirstHandler>();
            s.AddIntegrationEventHandler<OtherEvent, OtherHandler>();
        });
        await bus.PublishAsync(new TestEvent("x"));
        Assert.Equal(new[] { "first" }, log.Calls);
    }

    [Fact]
    public async Task E7_EachPublish_UsesNewScope()
    {
        var (bus, log) = Build(s => s.AddIntegrationEventHandler<TestEvent, FirstHandler>());
        await bus.PublishAsync(new TestEvent("a"));
        await bus.PublishAsync(new TestEvent("b"));
        Assert.Equal(2, log.ScopeIds.Count);
        Assert.NotEqual(log.ScopeIds[0], log.ScopeIds[1]);
    }

    [Fact]
    public async Task Publish_PassesCancellationTokenToHandlers()
    {
        var (bus, log) = Build(s => s.AddIntegrationEventHandler<TestEvent, FirstHandler>());
        using var cts = new CancellationTokenSource();
        await bus.PublishAsync(new TestEvent("x"), cts.Token);
        Assert.Equal(cts.Token, log.LastToken);
    }

    [Fact]
    public async Task Publish_DispatchesByStaticType()
    {
        var (bus, log) = Build(s => s.AddIntegrationEventHandler<TestEvent, FirstHandler>());
        IIntegrationEvent e = new TestEvent("x");
        await bus.PublishAsync(e);
        Assert.Empty(log.Calls);
    }

    [Fact]
    public void IntegrationEvent_HasUniqueIdAndUtcTimestamp()
    {
        var a = new TestEvent("x");
        var b = new TestEvent("x");
        Assert.NotEqual(a.EventId, b.EventId);
        Assert.NotEqual(Guid.Empty, a.EventId);
        Assert.Equal(DateTimeKind.Utc, a.OccurredOnUtc.Kind);
    }

    [Fact]
    public void AddInMemoryEventBus_RegistersSingleton()
    {
        var services = new ServiceCollection().AddLogging().AddInMemoryEventBus();
        var d = Assert.Single(services, x => x.ServiceType == typeof(IEventBus));
        Assert.Equal(ServiceLifetime.Singleton, d.Lifetime);
    }

    [Fact]
    public void AddIntegrationEventHandler_RegistersScoped()
    {
        var services = new ServiceCollection().AddIntegrationEventHandler<TestEvent, SecondHandler>();
        var d = Assert.Single(services, x => x.ServiceType == typeof(IIntegrationEventHandler<TestEvent>));
        Assert.Equal(ServiceLifetime.Scoped, d.Lifetime);
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, Exception? Exception)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Capture(this);
    public void Dispose() { }

    private sealed class Capture(CapturingLoggerProvider p) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => p.Entries.Add((logLevel, exception));
    }
}
