using EnglishCenter.Modules.Finance.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Finance.Api.Controllers;

[ApiController]
[Route("api/finance")]
public sealed class FinancePingController : ControllerBase
{
    private readonly IFinanceModuleInfo _moduleInfo;

    public FinancePingController(IFinanceModuleInfo moduleInfo)
    {
        _moduleInfo = moduleInfo;
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { module = _moduleInfo.ModuleName, schema = _moduleInfo.Schema });
}
