using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

[ApiController, Route("api/enrollment/enrollments"), Authorize, EnrollmentErrors]
public sealed class EnrollmentsController(EnrollmentService service) : ControllerBase
{
    private const string Managers = Roles.Staff + "," + Roles.Admin;

    [HttpPost, Authorize(Roles = Managers)]
    public async Task<IActionResult> Create(EnrollRequest request, CancellationToken ct)
    {
        var value = await service.EnrollAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = value.Id }, value);
    }

    [HttpGet("{id:guid}"), Authorize(Roles = Managers + "," + Roles.Student)]
    public Task<EnrollmentResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    // Đối soát thủ công khi event bus trong bộ nhớ đã bỏ qua handler lỗi.
    [HttpPost("{id:guid}/replay"), Authorize(Roles = Managers)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken ct)
    {
        await service.ReplayAsync(id, ct);
        return Accepted();
    }
}

public sealed class EnrollmentErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not EnrollmentFailure failure) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = failure.Status, Title = failure.Message })
            { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
