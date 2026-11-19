using EnglishCenter.Modules.MediaNotification.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.MediaNotification.Api.Controllers;

[ApiController, Route("api/media-notification/notifications"), Authorize(Roles = MediaRoles.Everyone), MediaErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    [HttpGet]
    public Task<NotificationListResponse> List(CancellationToken ct, [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => service.ListAsync(unreadOnly, page, pageSize, ct);

    [HttpGet("unread-count")]
    public Task<UnreadCountResponse> UnreadCount(CancellationToken ct) => service.UnreadCountAsync(ct);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await service.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public Task<MarkedReadResponse> MarkAllRead(CancellationToken ct) => service.MarkAllReadAsync(ct);
}
