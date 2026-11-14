using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

[ApiController, Route("api/enrollment/statistics/mine"), Authorize(Roles = Roles.Student), EnrollmentErrors]
public sealed class EnrollmentStatisticsController(EnrollmentService service) : ControllerBase
{
    [HttpGet]
    public Task<EnrollmentStatisticsResponse> Get(CancellationToken ct) =>
        service.GetStatisticsAsync(ct);
}
