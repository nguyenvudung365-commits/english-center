using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Domain.Invoices;

namespace EnglishCenter.Modules.Finance.Application.Invoices;

public sealed record InvoicePage(IReadOnlyList<InvoiceResponse> Items, int Total, int Page, int PageSize);
public interface IInvoiceManagementStore
{
    Task<Invoice?> FindAsync(Guid id, CancellationToken ct);
    Task<InvoicePage> ListAsync(Guid? student, int page, int size, CancellationToken ct);
    Task<Invoice> MarkPaidAsync(Guid id, Guid confirmedBy, DateTime paidAt, CancellationToken ct);
}

public sealed class InvoiceManagement(IInvoiceManagementStore store, InvoiceQuery query, IEventBus events, TimeProvider clock)
{
    public async Task<InvoiceResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var student = await query.StudentFilterAsync(null, ct);
        var x = await store.FindAsync(id, ct);
        if (x is null || (student.HasValue && student != x.StudentId)) throw new FinanceFailure(404, "Invoice not found.");
        return InvoiceQuery.Response(x);
    }
    public async Task<InvoicePage> ListAsync(Guid? filter, int page, int size, CancellationToken ct)
    {
        var student = await query.StudentFilterAsync(filter, ct);
        if (page < 1 || page > 1_000_000 || size < 1) throw new FinanceFailure(400, "Invalid pagination.");
        return await store.ListAsync(student, page, Math.Min(size, 100), ct);
    }
    public async Task<InvoiceResponse> MarkPaidAsync(Guid id, CancellationToken ct)
    {
        var actor = await query.EnsureManagerAsync(ct);
        var utc = clock.GetUtcNow().UtcDateTime;
        var value = await store.MarkPaidAsync(id, actor, new(utc.Ticks / 10 * 10, DateTimeKind.Utc), ct);
        await PublishAsync(value, ct);
        return InvoiceQuery.Response(value);
    }
    public async Task ReplayPaymentAsync(Guid id, CancellationToken ct)
    {
        await query.EnsureManagerAsync(ct);
        var value = await store.FindAsync(id, ct) ?? throw new FinanceFailure(404, "Invoice not found.");
        if (value.Status != "Paid") throw new FinanceFailure(409, "Invoice has not been paid.");
        await PublishAsync(value, ct);
    }
    private Task PublishAsync(Invoice x, CancellationToken ct) => events.PublishAsync(
        new InvoicePaidIntegrationEvent(x.Id, x.EnrollmentId, x.StudentId, x.PaidAt!.Value)
        { EventId = x.Id, OccurredOnUtc = x.PaidAt.Value }, ct);
}
