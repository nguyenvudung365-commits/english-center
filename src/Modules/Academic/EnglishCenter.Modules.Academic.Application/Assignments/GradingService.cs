using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

/// <summary>
/// US-21: chấm và sửa điểm 0–10 (bước 0,1) kèm nhận xét. Chỉ Teacher đúng lớp được ghi; mỗi lần ghi tạo một dòng lịch sử
/// (điểm trước/sau, nhận xét, người, thời gian, version) trong cùng transaction với điểm hiện tại.
/// </summary>
public sealed class GradingService(IAssignmentStore store, IAcademicStore classes, CourseworkAccess access, TimeProvider clock,
    CourseworkEvents events, ILogger<GradingService> log)
{
    public async Task<SubmissionResponse> GradeAsync(Guid submissionId, GradeSubmissionRequest request, CancellationToken ct)
    {
        var userId = access.UserId();
        if (!access.IsTeacher) throw new AcademicFailure(403, "Only the class teacher grades submissions.");
        var submission = await store.FindSubmissionByIdAsync(submissionId, ct) ?? throw new AcademicFailure(404, "Submission not found.");
        var assignment = await store.FindAssignmentAsync(submission.AssignmentId, ct) ?? throw new AcademicFailure(404, "Assignment not found.");
        await access.AuthorizeAsync(assignment.ClassId, ClassAccess.Teach, ct);

        var errors = new Dictionary<string, string[]>();
        if (request.Score is not { } score || !Submission.IsValidScore(score))
            errors["score"] = ["Điểm từ 0 đến 10, bước 0,1."];
        if (request.Feedback?.Trim().Length > Submission.FeedbackMaxLength)
            errors["feedback"] = [$"Nhận xét tối đa {Submission.FeedbackMaxLength} ký tự."];
        if (request.ExpectedGradeVersion is null or < 0)
            errors["expectedGradeVersion"] = ["Gửi gradeVersion đang xem (0 nếu chưa chấm)."];
        if (errors.Count > 0) throw new AcademicFailure(400, "Invalid grade.", errors);

        var value = request.Score!.Value;
        var expected = request.ExpectedGradeVersion!.Value;
        // Retry đúng nội dung sau khi mất response: trả kết quả đã lưu, không thêm lịch sử/event.
        if (submission.IsSameGrade(value, request.Feedback, userId, expected)) return AssignmentService.Response(submission);

        SubmissionGrade grade;
        try { grade = submission.Grade(value, request.Feedback, userId, expected, clock.GetUtcNow()); }
        catch (GradeConflictException) { throw new AcademicFailure(409, "The grade changed. Reload before grading again."); }
        store.Add(grade);
        await store.SaveAsync(ct);

        await events.PublishGradeAsync(submission, assignment.ClassId, grade, ct);
        grade.MarkEventSent(clock.GetUtcNow());
        try { await store.SaveAsync(ct); }
        catch (AcademicFailure ex) { log.LogWarning(ex, "Could not mark SubmissionGraded {SubmissionId} v{Version} as sent", submission.Id, grade.Version); }
        return AssignmentService.Response(submission);
    }

    public async Task<IReadOnlyList<GradeHistoryResponse>> HistoryAsync(Guid submissionId, CancellationToken ct)
    {
        var userId = access.UserId();
        var submission = await store.FindSubmissionByIdAsync(submissionId, ct) ?? throw new AcademicFailure(404, "Submission not found.");
        var assignment = await store.FindAssignmentAsync(submission.AssignmentId, ct) ?? throw new AcademicFailure(404, "Assignment not found.");
        if (access.IsStudent && submission.StudentId != userId) throw new AcademicFailure(403, "This is another student's submission.");
        await access.AuthorizeAsync(assignment.ClassId, access.IsStudent ? ClassAccess.Read : ClassAccess.Manage, ct);
        var names = new Dictionary<Guid, string?>();
        var result = new List<GradeHistoryResponse>();
        foreach (var x in (await store.GradeHistoryAsync(submissionId, ct)).OrderByDescending(x => x.Version))
        {
            if (!names.TryGetValue(x.GradedBy, out var name))
                names[x.GradedBy] = name = (await classes.FindTeacherAsync(x.GradedBy, ct))?.FullName;
            result.Add(new(x.Version, x.Score, x.PreviousScore, x.Feedback, x.GradedBy, name, x.GradedAt));
        }
        return result;
    }
}
