using EnglishCenter.Modules.MediaNotification.Application.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.MediaNotification.Api.Controllers;

[ApiController]
[Route("api/media-notification/files")]
[Authorize]
[FileErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FilesController(FileService files) : ControllerBase
{
    [Authorize, HttpPost("presign")]
    public Task<UploadTicket> Presign(PresignUploadRequest request, CancellationToken ct) =>
        files.PresignUploadAsync(request, ct);

    [Authorize, HttpPost("{fileId:guid}/confirm")]
    public Task<FileResponse> Confirm(Guid fileId, CancellationToken ct) => files.ConfirmUploadAsync(fileId, ct);

    [Authorize, HttpGet("{fileId:guid}/download")]
    public Task<DownloadTicket> Download(Guid fileId, CancellationToken ct) => files.PresignDownloadAsync(fileId, ct);
}

public sealed class FileErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not FileFailure failure) return;
        var body = new ProblemDetails
        {
            Status = failure.Status, Title = failure.Message,
            Type = $"https://httpstatuses.io/{failure.Status}", Instance = context.HttpContext.Request.Path
        };
        context.Result = new ObjectResult(body) { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
