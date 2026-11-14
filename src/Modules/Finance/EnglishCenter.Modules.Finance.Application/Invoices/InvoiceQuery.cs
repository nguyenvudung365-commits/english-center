using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Domain.Invoices;

namespace EnglishCenter.Modules.Finance.Application.Invoices;

public sealed record InvoiceResponse(Guid Id, string InvoiceNo, Guid EnrollmentId, Guid StudentId, Guid ClassId,
    decimal Amount, string Status, DateTime IssuedAtUtc, DateOnly DueDate, DateTime? PaidAtUtc = null, Guid? ConfirmedBy = null);

public sealed class InvoiceQuery(IInvoiceStore store)
{
    public async Task<InvoiceResponse?> GetAsync(Guid enrollmentId, Guid? student, CancellationToken ct)
    {
        var x = await store.FindByEnrollmentAsync(enrollmentId, ct);
        if (x is null || (student.HasValue && student != x.StudentId)) return null;
        return Response(x);
    }

    public static InvoiceResponse Response(Invoice x) => new(x.Id, x.InvoiceNo, x.EnrollmentId, x.StudentId, x.ClassId,
        x.Amount, x.Status, x.IssuedAt, x.DueDate, x.PaidAt, x.ConfirmedBy);
}
