using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Identity.Contracts;

namespace EnglishCenter.Modules.Finance.Application.Invoices;

public sealed record InvoiceResponse(Guid Id, string InvoiceNo, Guid EnrollmentId, Guid StudentId, Guid ClassId,
    decimal Amount, string Status, DateTime IssuedAtUtc, DateOnly DueDate);

public sealed class FinanceFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class InvoiceQuery(IInvoiceStore store, ICurrentUser current, IAccountAccess accounts)
{
    public async Task<InvoiceResponse?> GetAsync(Guid enrollmentId, CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is null || current.UserId == Guid.Empty)
            throw new FinanceFailure(401, "Authentication required.");
        if (current.Role is not (Roles.Admin or Roles.Staff or Roles.Student) ||
            !await accounts.IsActiveInRoleAsync(current.UserId.Value, current.Role, ct))
            throw new FinanceFailure(403, "An active account with the current role is required.");
        Guid? student = current.Role == Roles.Student ? current.UserId : null;
        var x = await store.FindByEnrollmentAsync(enrollmentId, ct);
        if (x is null || (student.HasValue && student != x.StudentId)) return null;
        return new(x.Id, x.InvoiceNo, x.EnrollmentId, x.StudentId, x.ClassId, x.Amount, x.Status, x.IssuedAt, x.DueDate);
    }
}
