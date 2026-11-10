using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Domain.Invoices;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Thay kiểm thử recorder mẫu tuần 1 bằng kiểm thử handler hóa đơn thật tuần 3.
public sealed class Tv3Week1Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1_500_000)]
    [InlineData(999_999_999_999)]
    public async Task FinanceHandler_PreservesPayloadAndDueDate(long tuition)
    {
        var store = new CapturingStore();
        var bus = new CapturingBus();
        var handler = new StudentEnrolledIntegrationEventHandler(store, bus, TimeProvider.System);
        var enrolled = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tuition,
            new DateTime(2027, 1, 31, 23, 59, 0, DateTimeKind.Utc));
        using var cancellation = new CancellationTokenSource();
        await handler.HandleAsync(enrolled, cancellation.Token);
        Assert.Equal(enrolled.EnrollmentId, store.Value!.EnrollmentId);
        Assert.Equal(enrolled.StudentId, store.Value.StudentId);
        Assert.Equal(enrolled.ClassId, store.Value.ClassId);
        Assert.Equal(tuition, store.Value.Amount);
        Assert.Equal(new DateOnly(2027, 2, 8), store.Value.DueDate);
        Assert.Equal(cancellation.Token, store.Token);
        var published = Assert.IsType<InvoiceCreatedIntegrationEvent>(bus.Value);
        Assert.Equal(store.Value.Id, published.InvoiceId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(1_000_000_000_000)]
    public void InvalidAmount_IsRejected(decimal amount) => Assert.Throws<ArgumentException>(() =>
        Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, DateTime.UtcNow, DateTime.UtcNow));

    [Theory]
    [InlineData("2026-12-31T16:59:00Z", "2027-01-07")]
    [InlineData("2026-12-31T17:00:00Z", "2027-01-08")]
    [InlineData("2028-02-22T17:00:00Z", "2028-03-01")]
    public void DueDate_UsesVietnamCalendar(string utc, string expected)
    {
        var at = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(DateOnly.Parse(expected), Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, at, DateTime.UtcNow).DueDate);
    }

    private sealed class CapturingStore : IInvoiceStore
    {
        public Invoice? Value { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<Invoice> CreateOnceAsync(Invoice invoice, CancellationToken ct)
        { Value = invoice; Token = ct; return Task.FromResult(invoice); }
        public Task<Invoice?> FindByEnrollmentAsync(Guid id, CancellationToken ct) => Task.FromResult(Value);
    }
    private sealed class CapturingBus : IEventBus
    {
        public IIntegrationEvent? Value { get; private set; }
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default) where TEvent : IIntegrationEvent
        { Value = integrationEvent; return Task.CompletedTask; }
    }
}
