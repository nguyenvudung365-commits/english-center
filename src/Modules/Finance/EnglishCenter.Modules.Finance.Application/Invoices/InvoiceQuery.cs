using EnglishCenter.Modules.Finance.Application.Abstractions;

namespace EnglishCenter.Modules.Finance.Application.Invoices;

public sealed record InvoiceResponse(Guid Id, string InvoiceNo, Guid EnrollmentId, Guid StudentId, Guid ClassId,
    decimal Amount, string Status, DateTime IssuedAtUtc, DateOnly DueDate);

public sealed class InvoiceQuery(IInvoiceStore store)
{
    public async Task<InvoiceResponse?> GetAsync(Guid enrollmentId, Guid? student, CancellationToken ct)
    {
        var x = await store.FindByEnrollmentAsync(enrollmentId, ct);
        if (x is null || (student.HasValue && student != x.StudentId)) return null;
        return new(x.Id, x.InvoiceNo, x.EnrollmentId, x.StudentId, x.ClassId, x.Amount, x.Status, x.IssuedAt, x.DueDate);
    }
}
