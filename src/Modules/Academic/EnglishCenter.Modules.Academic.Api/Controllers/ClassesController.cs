using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Classes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

internal static class AcademicRoles
{
    // Giáo vụ (Staff) và Admin quản lý lớp; giáo viên/học viên xem lớp của mình ở US-09/US-11.
    public const string Managers = Roles.Staff + "," + Roles.Admin;
    public const string Readers = Managers + "," + Roles.Teacher + "," + Roles.Student;
}

[ApiController]
[Route("api/academic/classes")]
[Authorize(Roles = AcademicRoles.Readers)]
[AcademicErrors]
public sealed class ClassesController(ClassService classes) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = AcademicRoles.Managers)]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateClassRequest request, CancellationToken ct)
    {
        var created = await classes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClassResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ClassResponse> Get(Guid id, CancellationToken ct) => classes.GetAsync(id, ct);

    [HttpGet]
    public Task<ClassPageResponse> List([FromQuery] ClassListQuery query, CancellationToken ct) => classes.ListAsync(query, ct);

    [HttpPost("{id:guid}/replay")]
    [Authorize(Roles = AcademicRoles.Managers)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken ct)
    {
        await classes.ReplayAsync(id, ct);
        return Accepted();
    }

    [HttpPost("republish")]
    [Authorize(Roles = AcademicRoles.Managers)]
    [ProducesResponseType<RepublishClassesResponse>(StatusCodes.Status200OK)]
    public Task<RepublishClassesResponse> Republish(CancellationToken ct) => classes.RepublishAsync(ct);
}

// Danh mục để giáo vụ chọn courseId/teacherId khi tạo lớp.
[ApiController]
[Route("api/academic")]
[Authorize(Roles = AcademicRoles.Managers)]
[AcademicErrors]
public sealed class AcademicLookupController(ClassService classes) : ControllerBase
{
    [HttpGet("courses")]
    public Task<IReadOnlyList<CourseResponse>> Courses(CancellationToken ct) => classes.CoursesAsync(ct);

    [HttpGet("teachers")]
    public Task<IReadOnlyList<TeacherResponse>> Teachers(CancellationToken ct) => classes.TeachersAsync(ct);
}

public sealed class AcademicErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not AcademicFailure failure) return;
        ProblemDetails problem = failure.Errors is null
            ? new ProblemDetails { Status = failure.Status, Title = failure.Message }
            : new ValidationProblemDetails(failure.Errors.ToDictionary(x => x.Key, x => x.Value))
            { Status = failure.Status, Title = failure.Message };
        context.Result = new ObjectResult(problem) { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
