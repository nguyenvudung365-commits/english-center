using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.MediaNotification.Api.Controllers;

[ApiController, Route("api/media-notification"), Authorize(Roles = MediaRoles.Everyone), MediaErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClassFeedController(ClassFeedService service) : ControllerBase
{
    [HttpGet("classes/{classId:guid}/posts")]
    public Task<PagedResult<PostResponse>> Posts(Guid classId, CancellationToken ct, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) => service.PostsAsync(classId, page, pageSize, ct);

    [HttpPost("classes/{classId:guid}/posts")]
    public async Task<IActionResult> CreatePost(Guid classId, ContentRequest request, CancellationToken ct)
    {
        var value = await service.CreatePostAsync(classId, request, ct);
        return Created($"/api/media-notification/classes/{classId}/posts", value);
    }

    [HttpPut("posts/{id:guid}")]
    public async Task<IActionResult> EditPost(Guid id, ContentRequest request, CancellationToken ct)
    {
        await service.EditPostAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("posts/{id:guid}")]
    public async Task<IActionResult> RemovePost(Guid id, CancellationToken ct)
    {
        await service.RemovePostAsync(id, ct);
        return NoContent();
    }

    [HttpPut("posts/{id:guid}/hidden")]
    public async Task<IActionResult> HidePost(Guid id, HiddenRequest request, CancellationToken ct)
    {
        await service.SetPostHiddenAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("posts/{id:guid}/like")]
    public Task<LikeResponse> Like(Guid id, CancellationToken ct) => service.SetLikeAsync(id, true, ct);

    [HttpDelete("posts/{id:guid}/like")]
    public Task<LikeResponse> Unlike(Guid id, CancellationToken ct) => service.SetLikeAsync(id, false, ct);

    [HttpGet("posts/{id:guid}/comments")]
    public Task<PagedResult<CommentResponse>> Comments(Guid id, CancellationToken ct, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20) => service.CommentsAsync(id, page, pageSize, ct);

    [HttpPost("posts/{id:guid}/comments")]
    public async Task<IActionResult> CreateComment(Guid id, ContentRequest request, CancellationToken ct)
    {
        var value = await service.CreateCommentAsync(id, request, ct);
        return Created($"/api/media-notification/posts/{id}/comments", value);
    }

    [HttpPut("comments/{id:guid}")]
    public Task<CommentResponse> EditComment(Guid id, ContentRequest request, CancellationToken ct) =>
        service.EditCommentAsync(id, request, ct);

    [HttpDelete("comments/{id:guid}")]
    public async Task<IActionResult> RemoveComment(Guid id, CancellationToken ct)
    {
        await service.RemoveCommentAsync(id, ct);
        return NoContent();
    }

    [HttpPut("comments/{id:guid}/hidden")]
    public async Task<IActionResult> HideComment(Guid id, HiddenRequest request, CancellationToken ct)
    {
        await service.SetCommentHiddenAsync(id, request, ct);
        return NoContent();
    }
}
