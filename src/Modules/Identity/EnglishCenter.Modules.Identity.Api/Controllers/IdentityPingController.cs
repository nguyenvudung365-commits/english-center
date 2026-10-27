using EnglishCenter.Modules.Identity.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityPingController : ControllerBase
{
    private readonly IIdentityModuleInfo _moduleInfo;

    public IdentityPingController(IIdentityModuleInfo moduleInfo)
    {
        _moduleInfo = moduleInfo;
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { module = _moduleInfo.ModuleName, schema = _moduleInfo.Schema });
}
