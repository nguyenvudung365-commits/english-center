using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EnglishCenter.Modules.MediaNotification.Api.Controllers;

internal static class MediaRoles
{
    public const string Everyone = Roles.Student + "," + Roles.Teacher + "," + Roles.Staff + "," + Roles.Admin;
    public const string FeedbackReaders = Roles.Student + "," + Roles.Staff + "," + Roles.Admin;
}

public sealed class MediaErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not MediaFailure failure) return;
        var body = new ProblemDetails
        {
            Status = failure.Status, Title = failure.Message,
            Type = $"https://httpstatuses.io/{failure.Status}", Instance = context.HttpContext.Request.Path
        };
        context.Result = new ObjectResult(body) { StatusCode = failure.Status };
        context.ExceptionHandled = true;
    }
}
