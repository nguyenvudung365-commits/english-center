using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Application.Faqs;
using EnglishCenter.Modules.MediaNotification.Application.Feedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.MediaNotification.Api.Controllers;

[ApiController, Route("api/media-notification/feedback"), Authorize(Roles = MediaRoles.FeedbackReaders), MediaErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FeedbackController(FeedbackService service) : ControllerBase
{
    [HttpPost, Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Create(FeedbackRequest request, CancellationToken ct)
    {
        var value = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = value.Id }, value);
    }

    [HttpGet]
    public Task<PagedResult<FeedbackResponse>> List(CancellationToken ct, [FromQuery] string? status = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        service.ListAsync(status, search, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<FeedbackResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPatch("{id:guid}/status"), Authorize(Roles = Roles.AccountManagers)]
    public Task<FeedbackResponse> UpdateStatus(Guid id, FeedbackStatusRequest request, CancellationToken ct) =>
        service.UpdateStatusAsync(id, request, ct);
}

[ApiController, Route("api/media-notification/faqs"), MediaErrors]
public sealed class FaqsController(FaqService service) : ControllerBase
{
    // Khách (chưa đăng nhập) chỉ đọc/tìm FAQ đã xuất bản.
    [HttpGet, AllowAnonymous]
    public Task<PagedResult<FaqResponse>> List(CancellationToken ct, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => service.ListPublishedAsync(search, page, pageSize, ct);
}

[ApiController, Route("api/media-notification/admin/faqs"), Authorize(Roles = Roles.Admin), MediaErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminFaqsController(FaqService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<FaqResponse>> List(CancellationToken ct, [FromQuery] bool? published = null,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        service.ListAllAsync(published, search, page, pageSize, ct);

    [HttpPost]
    public async Task<IActionResult> Create(FaqRequest request, CancellationToken ct)
    {
        var value = await service.CreateAsync(request, ct);
        return Created($"/api/media-notification/admin/faqs/{value.Id}", value);
    }

    [HttpPut("{id:guid}")]
    public Task<FaqResponse> Update(Guid id, FaqRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
