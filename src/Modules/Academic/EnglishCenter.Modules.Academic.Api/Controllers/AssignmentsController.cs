using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Assignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController, Route("api/academic/assignments"), Authorize, AcademicErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssignmentsController(AssignmentService service) : ControllerBase
{
    private const string Managers = Roles.Teacher + "," + Roles.Admin + "," + Roles.Staff;

    [HttpPost, Authorize(Roles = Managers)]
    public async Task<IActionResult> Create(CreateAssignmentRequest request, CancellationToken ct)
    {
        var item = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }
    [HttpGet]
    public Task<IReadOnlyList<AssignmentResponse>> List([FromQuery] Guid classId, [FromQuery] string? search, [FromQuery] string? status,
        [FromQuery] string? kind, CancellationToken ct) => service.ListAsync(classId, new AssignmentQuery(search, status, kind), ct);
    // US-22: bài và điểm của học viên đang đăng nhập, lọc theo lớp/trạng thái/loại/tìm kiếm, có phân trang.
    [HttpGet("mine"), Authorize(Roles = Roles.Student)]
    public Task<PageResponse<StudentAssignmentResponse>> Mine([FromQuery] Guid? classId, [FromQuery] string? search,
        [FromQuery] string? status, [FromQuery] string? submissionStatus, [FromQuery] string? kind,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        service.StudentAssignmentsAsync(new StudentAssignmentQuery(classId, search, status, submissionStatus, kind, page, pageSize), ct);
    [HttpGet("{id:guid}")]
    public Task<AssignmentResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, ct);
    [HttpPut("{id:guid}"), Authorize(Roles = Managers)]
    public Task<AssignmentResponse> Update(Guid id, UpdateAssignmentRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);
    [HttpPost("{id:guid}/publish"), Authorize(Roles = Managers)]
    public Task<AssignmentResponse> Publish(Guid id, [FromBody] PublishAssignmentRequest? request, CancellationToken ct) =>
        service.PublishAsync(id, request ?? new PublishAssignmentRequest(null), ct);
    [HttpGet("{id:guid}/recipients"), Authorize(Roles = Managers)]
    public Task<IReadOnlyList<RecipientResponse>> Recipients(Guid id, CancellationToken ct) => service.RecipientsAsync(id, ct);
    [HttpPost("{id:guid}/events/replay"), Authorize(Roles = Roles.AccountManagers)]
    public Task<ReplayEventsResponse> Replay(Guid id, CancellationToken ct) => service.ReplayEventsAsync(id, ct);
    [HttpPut("{id:guid}/submissions"), Authorize(Roles = Roles.Student)]
    public Task<SubmissionResponse> Submit(Guid id, SubmitAssignmentRequest request, CancellationToken ct) => service.SubmitAsync(id, request, ct);
    [HttpGet("{id:guid}/submissions/mine"), Authorize(Roles = Roles.Student)]
    public Task<SubmissionResponse> MySubmission(Guid id, CancellationToken ct) => service.MineAsync(id, ct);
    [HttpGet("{id:guid}/submissions"), Authorize(Roles = Managers)]
    public Task<IReadOnlyList<SubmissionResponse>> Submissions(Guid id, [FromQuery] bool? graded, CancellationToken ct) =>
        service.ListSubmissionsAsync(id, graded, ct);
}

[ApiController, Route("api/academic/submissions"), Authorize, AcademicErrors]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SubmissionFilesController(AssignmentService service, GradingService grading) : ControllerBase
{
    [HttpGet("{id:guid}/files/{fileId:guid}/download")]
    public Task<FileDownloadResponse> Download(Guid id, Guid fileId, CancellationToken ct) => service.DownloadAsync(id, fileId, ct);
    // US-21: chấm lần đầu hoặc sửa điểm (Teacher đúng lớp), có kiểm tra gradeVersion.
    [HttpPut("{id:guid}/grade"), Authorize(Roles = Roles.Teacher)]
    public Task<SubmissionResponse> Grade(Guid id, GradeSubmissionRequest request, CancellationToken ct) => grading.GradeAsync(id, request, ct);
    [HttpGet("{id:guid}/grades")]
    public Task<IReadOnlyList<GradeHistoryResponse>> GradeHistory(Guid id, CancellationToken ct) => grading.HistoryAsync(id, ct);
}
