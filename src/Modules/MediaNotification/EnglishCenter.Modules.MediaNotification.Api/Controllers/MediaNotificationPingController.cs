using EnglishCenter.Modules.MediaNotification.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.MediaNotification.Api.Controllers;

[ApiController]
[Route("api/media-notification")]
public sealed class MediaNotificationPingController : ControllerBase
{
    private readonly IMediaNotificationModuleInfo _moduleInfo;

    public MediaNotificationPingController(IMediaNotificationModuleInfo moduleInfo)
    {
        _moduleInfo = moduleInfo;
    }

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { module = _moduleInfo.ModuleName, schema = _moduleInfo.Schema });
}
