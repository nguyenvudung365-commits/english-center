using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using EnglishCenter.Modules.Finance.Application.Invoices;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

internal sealed class InvoiceStore(FinanceDbContext db) : IInvoiceStore, IInvoiceManagementStore
{
    public Task<Invoice?> FindAsync(Guid id, CancellationToken ct) => db.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<InvoicePage> ListAsync(Guid? student, int page, int size, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking();
        if (student.HasValue) query = query.Where(x => x.StudentId == student);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.IssuedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items.Select(InvoiceQuery.Response).ToList(), total, page, size);
    }

    public async Task<Invoice> MarkPaidAsync(Guid id, Guid confirmedBy, DateTime paidAt, CancellationToken ct)
    {
        var changed = await db.Invoices.Where(x => x.Id == id && x.Status == "Unpaid")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Paid").SetProperty(x => x.PaidAt, paidAt)
                .SetProperty(x => x.ConfirmedBy, confirmedBy), ct);
        var value = await FindAsync(id, ct) ?? throw new FinanceFailure(404, "Invoice not found.");
        if (changed == 0) throw new FinanceFailure(409, "Invoice is already paid.");
        return value;
    }

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
