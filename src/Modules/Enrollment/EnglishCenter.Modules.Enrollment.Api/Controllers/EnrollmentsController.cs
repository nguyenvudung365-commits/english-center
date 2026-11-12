using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Enrollment.Application.Enrollments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

[ApiController, Route("api/enrollment/enrollments"), Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
[EnrollmentErrors]
public sealed class EnrollmentsController(EnrollmentService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateEnrollmentRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return Created($"/api/enrollment/enrollments?classId={result.ClassId}", result);
    }
    [HttpGet]
    public Task<IReadOnlyList<EnrollmentResponse>> List([FromQuery] Guid classId, CancellationToken ct) => service.ListAsync(classId, ct);
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) { await service.RevokeAsync(id, ct); return NoContent(); }
}

public sealed class EnrollmentErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not EnrollmentFailure failure) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = failure.Status, Title = failure.Message }) { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
