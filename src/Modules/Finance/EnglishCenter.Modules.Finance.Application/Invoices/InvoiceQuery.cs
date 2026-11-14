using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Domain.Invoices;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Finance.Application.Invoices;

public sealed record InvoiceResponse(Guid Id, string InvoiceNo, Guid EnrollmentId, Guid StudentId, Guid ClassId,
    decimal Amount, string Status, DateTime IssuedAtUtc, DateOnly DueDate, DateTime? PaidAtUtc = null, Guid? ConfirmedBy = null);

public sealed class FinanceFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class InvoiceQuery(IInvoiceStore store, ICurrentUser current, IAccountAccess accounts)
{
    internal async Task<Guid> EnsureReaderAsync(CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is not { } userId || userId == Guid.Empty)
            throw new FinanceFailure(401, "Authentication required.");
        if (current.Role is not (Roles.Admin or Roles.Staff or Roles.Student) ||
            !await accounts.IsActiveInRoleAsync(userId, current.Role, ct))
            throw new FinanceFailure(403, "An active account with the current role is required.");
        return userId;
    }

    internal async Task<Guid?> StudentFilterAsync(Guid? filter, CancellationToken ct)
    {
        var userId = await EnsureReaderAsync(ct);
        return current.Role == Roles.Student ? userId : filter;
    }

    internal async Task<Guid> EnsureManagerAsync(CancellationToken ct)
    {
        var userId = await EnsureReaderAsync(ct);
        if (current.Role is not (Roles.Admin or Roles.Staff))
            throw new FinanceFailure(403, "Only staff manage invoices.");
        return userId;
    }

    public async Task<InvoiceResponse?> GetAsync(Guid enrollmentId, CancellationToken ct)
    {
        var student = await StudentFilterAsync(null, ct);
        var x = await store.FindByEnrollmentAsync(enrollmentId, ct);
        if (x is null || (student.HasValue && student != x.StudentId)) return null;
        return Response(x);
    }

    public static InvoiceResponse Response(Invoice x) => new(x.Id, x.InvoiceNo, x.EnrollmentId, x.StudentId, x.ClassId,
        x.Amount, x.Status, x.IssuedAt, x.DueDate, x.PaidAt, x.ConfirmedBy);
}
