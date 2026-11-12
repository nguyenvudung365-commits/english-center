using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Classes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController, Route("api/academic/classes/mine"), Authorize(Roles = Roles.Student)]
public sealed class StudentClassesController(StudentClasses classes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StudentClassResponse>>> Get(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(AuthClaims.Subject)?.Value, out var student)) return Forbid();
        return Ok(await classes.ListAsync(student, ct));
    }
}
