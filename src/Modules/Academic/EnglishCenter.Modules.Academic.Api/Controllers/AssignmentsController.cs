using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Assignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController, Route("api/academic/assignments"), Authorize, AcademicErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssignmentsController(AssignmentService service) : ControllerBase
{
    [HttpPost, Authorize(Roles = Roles.Teacher + "," + Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Create(CreateAssignmentRequest request, CancellationToken ct)
    {
        var item = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }
    [HttpGet]
    public Task<IReadOnlyList<AssignmentResponse>> List([FromQuery] Guid classId, CancellationToken ct) => service.ListAsync(classId, ct);
    [HttpGet("{id:guid}")]
    public Task<AssignmentResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
    [HttpPut("{id:guid}/submissions"), Authorize(Roles = Roles.Student)]
    public Task<SubmissionResponse> Submit(Guid id, SubmitAssignmentRequest request, CancellationToken ct) => service.SubmitAsync(id, request, ct);
    [HttpGet("{id:guid}/submissions/mine"), Authorize(Roles = Roles.Student)]
    public Task<SubmissionResponse> Mine(Guid id, CancellationToken ct) => service.MineAsync(id, ct);
    [HttpGet("{id:guid}/submissions"), Authorize(Roles = Roles.Teacher + "," + Roles.Admin + "," + Roles.Staff)]
    public Task<IReadOnlyList<SubmissionResponse>> Submissions(Guid id, CancellationToken ct) => service.ListSubmissionsAsync(id, ct);
}

[ApiController, Route("api/academic/submissions"), Authorize, AcademicErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubmissionFilesController(AssignmentService service) : ControllerBase
{
    [HttpGet("{id:guid}/files/{fileId:guid}/download")]
    public Task<FileDownloadResponse> Download(Guid id, Guid fileId, CancellationToken ct) => service.DownloadAsync(id, fileId, ct);
}
