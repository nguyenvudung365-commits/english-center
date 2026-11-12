using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Finance.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.Finance.Api.Controllers;

[ApiController, Route("api/finance/invoices"), Authorize(Roles = Roles.Staff + "," + Roles.Admin + "," + Roles.Student), FinanceErrors]
public sealed class InvoicesController(InvoiceQuery query) : ControllerBase
{
    [HttpGet("by-enrollment/{enrollmentId:guid}")]
    public async Task<ActionResult<InvoiceResponse>> Get(Guid enrollmentId, CancellationToken ct)
    {
        var value = await query.GetAsync(enrollmentId, ct);
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
