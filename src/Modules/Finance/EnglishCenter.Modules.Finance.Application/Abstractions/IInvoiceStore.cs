using EnglishCenter.Modules.Finance.Domain.Invoices;

namespace EnglishCenter.Modules.Finance.Application.Abstractions;

public interface IInvoiceStore
{
    Task<Invoice> CreateOnceAsync(Invoice invoice, CancellationToken ct);
    Task<Invoice?> FindByEnrollmentAsync(Guid enrollmentId, CancellationToken ct);
}
