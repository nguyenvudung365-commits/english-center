using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Finance.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.Finance.Api.Controllers;

[ApiController, Route("api/finance/invoices"), Authorize(Roles = Roles.Staff + "," + Roles.Admin + "," + Roles.Student), FinanceErrors]
public sealed class InvoicesController(InvoiceQuery query, InvoiceManagement management) : ControllerBase
{
    private const string Managers = Roles.Staff + "," + Roles.Admin;
    private Guid Subject() => Guid.TryParse(User.FindFirst(AuthClaims.Subject)?.Value, out var id) && id != Guid.Empty
        ? id : throw new FinanceFailure(403, "Invalid user subject.");
    private Guid? Student(Guid? filter = null) => User.IsInRole(Roles.Staff) || User.IsInRole(Roles.Admin) ? filter : Subject();

    [HttpGet]
    public Task<InvoicePage> List(CancellationToken ct, [FromQuery] Guid? studentId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) => management.ListAsync(Student(studentId), page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<InvoiceResponse> GetById(Guid id, CancellationToken ct) => management.GetAsync(id, Student(), ct);

    [HttpPost("{id:guid}/pay"), Authorize(Roles = Managers)]
    public Task<InvoiceResponse> Pay(Guid id, CancellationToken ct) => management.MarkPaidAsync(id, Subject(), ct);

    [HttpPost("{id:guid}/payment/replay"), Authorize(Roles = Managers)]
    public async Task<IActionResult> ReplayPayment(Guid id, CancellationToken ct)
    { await management.ReplayPaymentAsync(id, ct); return Accepted(); }

    [HttpGet("by-enrollment/{enrollmentId:guid}")]
    public async Task<ActionResult<InvoiceResponse>> Get(Guid enrollmentId, CancellationToken ct)
    {
        Guid? student = null;
        if (!User.IsInRole(Roles.Staff) && !User.IsInRole(Roles.Admin))
        {
            if (!Guid.TryParse(User.FindFirst(AuthClaims.Subject)?.Value, out var id)) return Forbid();
            student = id;
        }
        var value = await query.GetAsync(enrollmentId, student, ct);
        return value is null ? NotFound() : Ok(value);
    }
}

public sealed class FinanceErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not FinanceFailure failure) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = failure.Status, Title = failure.Message })
            { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
