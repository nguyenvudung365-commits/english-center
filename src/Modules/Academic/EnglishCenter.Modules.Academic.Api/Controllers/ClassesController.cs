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
}

[ApiController]
[Route("api/academic/classes")]
[Authorize(Roles = AcademicRoles.Managers)]
[AcademicErrors]
public sealed class ClassesController(ClassService classes) : ControllerBase
{
    [HttpPost]
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
}

// Danh mục để giáo vụ chọn courseId/teacherId khi tạo lớp.
[ApiController]
[Route("api/academic")]
[Authorize(Roles = AcademicRoles.Managers)]
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
