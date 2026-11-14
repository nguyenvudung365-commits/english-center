using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.PlacementTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Enrollment.Api.Controllers;

[ApiController, Route("api/enrollment/placement-tests"), Authorize(Roles = EnrollmentAccess.Readers), EnrollmentErrors]
public sealed class PlacementTestsController(PlacementTestService service) : ControllerBase
{
    [HttpPost, Authorize(Roles = EnrollmentAccess.Managers)]
    public async Task<IActionResult> Create(PlacementTestRequest request, CancellationToken ct)
    {
        var value = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = value.Id }, value);
    }
    [HttpPut("{id:guid}"), Authorize(Roles = EnrollmentAccess.Managers)]
    public Task<PlacementTestResponse> Update(Guid id, UpdatePlacementTestRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);
    [HttpGet("{id:guid}")]
    public Task<PlacementTestResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
    [HttpGet]
    public Task<PageResult<PlacementTestResponse>> List(CancellationToken ct, [FromQuery] Guid? studentId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        service.ListAsync(studentId, page, pageSize, ct);
}
