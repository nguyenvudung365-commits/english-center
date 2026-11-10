using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Finance.Application.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Finance.Api.Controllers;

[ApiController, Route("api/finance/invoices"), Authorize(Roles = Roles.Staff + "," + Roles.Admin + "," + Roles.Student)]
public sealed class InvoicesController(InvoiceQuery query) : ControllerBase
{
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
