using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

internal static class EnrollmentAccess
{
    public const string Managers = Roles.Staff + "," + Roles.Admin;
    public const string Readers = Managers + "," + Roles.Student;
    public static Guid Subject(System.Security.Claims.ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(AuthClaims.Subject)?.Value, out var id) && id != Guid.Empty
            ? id : throw new EnrollmentFailure(403, "Invalid user subject.");
    public static Guid? Student(System.Security.Claims.ClaimsPrincipal user, Guid? filter = null) =>
        user.IsInRole(Roles.Staff) || user.IsInRole(Roles.Admin) ? filter : Subject(user);
}

[ApiController, Route("api/enrollment/enrollments/{id:guid}/limits"), Authorize(Roles = EnrollmentAccess.Readers), EnrollmentErrors]
public sealed class EnrollmentLimitsController(MonitoringService service) : ControllerBase
{
    [HttpGet]
    public Task<LimitsResponse> Get(Guid id, CancellationToken ct) => service.GetLimitsAsync(id, EnrollmentAccess.Student(User), ct);
    [HttpPut, Authorize(Roles = EnrollmentAccess.Managers)]
    public Task<LimitsResponse> Update(Guid id, LimitsRequest request, CancellationToken ct) => service.SetLimitsAsync(id, request, ct);
}

[ApiController, Route("api/enrollment/warnings"), Authorize(Roles = EnrollmentAccess.Readers), EnrollmentErrors]
public sealed class WarningsController(MonitoringService service) : ControllerBase
{
    [HttpGet]
    public Task<PageResult<WarningResponse>> List(CancellationToken ct, [FromQuery] Guid? studentId = null,
        [FromQuery] Guid? enrollmentId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        service.WarningsAsync(EnrollmentAccess.Student(User, studentId), enrollmentId, page, pageSize, ct);
    [HttpPost("{id:guid}/replay"), Authorize(Roles = EnrollmentAccess.Managers)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken ct)
    { await service.ReplayAsync(id, ct); return Accepted(); }
}
