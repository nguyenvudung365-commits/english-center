using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Enrollment.Application.Enrollments;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MembershipService = EnglishCenter.Modules.Enrollment.Application.Enrollments.EnrollmentService;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

[ApiController, Route("api/enrollment/memberships"), Authorize(Roles = Roles.Admin + "," + Roles.Staff), EnrollmentErrors]
public sealed class MembershipsController(MembershipService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateEnrollmentRequest request, CancellationToken ct)
    {
        var value = await service.CreateAsync(request, ct);
        return Created($"/api/enrollment/memberships?classId={value.ClassId}", value);
    }

    [HttpGet]
    public Task<IReadOnlyList<EnrollmentResponse>> List([FromQuery] Guid classId, CancellationToken ct) => service.ListAsync(classId, ct);

    [HttpPatch("{id:guid}/status")]
    public Task<EnrollmentResponse> SetStatus(Guid id, EnrollmentStatusRequest request, CancellationToken ct) =>
        service.SetActiveAsync(id, request.IsActive, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await service.RevokeAsync(id, ct);
        return NoContent();
    }
}
