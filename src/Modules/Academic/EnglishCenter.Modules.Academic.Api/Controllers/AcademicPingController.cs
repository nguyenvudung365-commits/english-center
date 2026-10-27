using EnglishCenter.Modules.Academic.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController]
[Route("api/academic")]
public sealed class AcademicPingController : ControllerBase
{
    private readonly IAcademicModuleInfo _moduleInfo;

    public AcademicPingController(IAcademicModuleInfo moduleInfo)
    {
        _moduleInfo = moduleInfo;
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { module = _moduleInfo.ModuleName, schema = _moduleInfo.Schema });
}
