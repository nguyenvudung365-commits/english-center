using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Application.Practice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

// US-32: luyện tập văn bản theo lớp.
[ApiController, Route("api/academic/practice"), Authorize, AcademicErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PracticeController(PracticeService service) : ControllerBase
{
    private const string Managers = Roles.Teacher + "," + Roles.Admin + "," + Roles.Staff;

    [HttpGet]
    public Task<PageResponse<PracticeSummaryResponse>> List([FromQuery] Guid classId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        service.ListAsync(classId, search, page, pageSize, ct);
    [HttpGet("{id:guid}")]
    public Task<PracticeResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
    [HttpPost, Authorize(Roles = Managers)]
    public async Task<IActionResult> Create(CreatePracticeRequest request, CancellationToken ct)
    {
        var item = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }
    [HttpPut("{id:guid}"), Authorize(Roles = Managers)]
    public Task<PracticeResponse> Update(Guid id, UpdatePracticeRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);
    [HttpDelete("{id:guid}"), Authorize(Roles = Managers)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int? expectedVersion, CancellationToken ct)
    {
        await service.DeleteAsync(id, expectedVersion, ct);
        return NoContent();
    }
}
