using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using EnglishCenter.Modules.Academic.Application.Attendance;
using EnglishCenter.Modules.Academic.Application.Classes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnglishCenter.Modules.Academic.Api.Controllers;

[ApiController, Route("api/academic/attendance"), Authorize(Roles = AcademicRoles.Readers), AcademicErrors]
public sealed class AttendanceController(AttendanceService attendance) : ControllerBase
{
    [HttpGet("/api/academic/sessions/{sessionId}/attendance")]
    public Task<RosterPage> Roster(Guid sessionId, [FromQuery] PageQuery query, CancellationToken ct) => attendance.RosterAsync(sessionId, query, ct);

    [HttpPut("/api/academic/sessions/{sessionId}/attendance/{studentId}")]
    public Task<AttendanceRow> Record(Guid sessionId, Guid studentId, RecordAttendanceBody body, CancellationToken ct) =>
        attendance.RecordAsync(sessionId, studentId, new(body.Status, body.ExpectedRevision), ct);

    [HttpGet]
    public Task<Page<AttendanceRow>> List([FromQuery] AttendanceQuery query, CancellationToken ct) => attendance.ListAsync(query, ct);

    [HttpGet("summary")]
    public Task<Summary> Summary([FromQuery] SummaryQuery query, CancellationToken ct) => attendance.SummaryAsync(query, ct);

    [HttpGet("{attendanceId}/history")]
    public Task<Page<AttendanceRevisionResponse>> History(Guid attendanceId, [FromQuery] PageQuery query, CancellationToken ct) => attendance.HistoryAsync(attendanceId, query, ct);

    [HttpPost("{attendanceId}/replay")]
    public Task<ReplayDispatchResponse> Replay(Guid attendanceId, ReplayAttendanceBody body, CancellationToken ct) =>
        attendance.ReplayAsync(attendanceId, new(body.Revision, body.Reason), ct);

    [HttpGet("{attendanceId}/replays")]
    public Task<Page<ReplayAttempt>> Replays(Guid attendanceId, [FromQuery] PageQuery query, CancellationToken ct) => attendance.ReplaysAsync(attendanceId, query, ct);
}

public sealed record RecordAttendanceBody([Required] string? Status, [property: JsonRequired] int? ExpectedRevision);
public sealed record ReplayAttendanceBody([Required] int? Revision, [Required] string? Reason);
