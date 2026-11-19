using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController, Route("api/academic/statistics"), Authorize(Roles = AcademicRoles.Readers), AcademicErrors]
public sealed class StatisticsController(StatisticsService statistics) : ControllerBase
{
    [HttpGet("/api/academic/classes/{classId}/progress")]
    public Task<ClassProgress> Progress(Guid classId, CancellationToken ct) => statistics.ProgressAsync(classId, ct);

    [HttpGet("mine"), Authorize(Roles = Roles.Student)]
    public Task<PersonalStatistics> Mine([FromQuery] string? month, CancellationToken ct) =>
        statistics.MineAsync(month ?? (Request.Query.ContainsKey("month") ? string.Empty : null), ct);
}
