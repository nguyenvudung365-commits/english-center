using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

internal sealed class InvoiceStore(FinanceDbContext db) : IInvoiceStore
{
    public Task<Invoice?> FindByEnrollmentAsync(Guid enrollmentId, CancellationToken ct) =>
        db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.EnrollmentId == enrollmentId, ct);

    public async Task<Invoice> CreateOnceAsync(Invoice invoice, CancellationToken ct)
    {
        var existing = await FindByEnrollmentAsync(invoice.EnrollmentId, ct);
        if (existing is not null) return Check(existing, invoice);
        db.Invoices.Add(invoice);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await FindByEnrollmentAsync(invoice.EnrollmentId, ct);
            if (existing is null) throw;
            return Check(existing, invoice);
        }
        return invoice;
    }

    private static Invoice Check(Invoice existing, Invoice candidate)
    {
        if (existing.StudentId != candidate.StudentId || existing.ClassId != candidate.ClassId ||
            existing.Amount != candidate.Amount || existing.DueDate != candidate.DueDate)
            throw new InvalidOperationException($"Conflicting StudentEnrolled for {candidate.EnrollmentId}.");
        return existing;
    }
}
