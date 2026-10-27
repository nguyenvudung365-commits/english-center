using EnglishCenter.Modules.Enrollment.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

[ApiController]
[Route("api/enrollment")]
public sealed class EnrollmentPingController : ControllerBase
{
    private readonly IEnrollmentModuleInfo _moduleInfo;

    public EnrollmentPingController(IEnrollmentModuleInfo moduleInfo)
    {
        _moduleInfo = moduleInfo;
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { module = _moduleInfo.ModuleName, schema = _moduleInfo.Schema });
}
