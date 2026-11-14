using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.MediaNotification.Contracts;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

public sealed class AssignmentService(IAssignmentStore store, CourseworkAccess access, ISubmissionFileAccess media,
    TimeProvider clock, SubmissionLimits limits, ISessionCatalog sessions, CourseworkEvents events, ILogger<AssignmentService> log)
{
    private static readonly string[] SubmissionStatuses = ["NotSubmitted", "Submitted", "Graded", "Missed", "Exempted"];

    private async Task<Assignment> FindAsync(Guid id, CancellationToken ct) =>
        await store.FindAssignmentAsync(id, ct) ?? throw new AcademicFailure(404, "Assignment not found.");

    // Học viên không thấy bài nháp: trả 404 như không tồn tại.
    private void EnsureVisible(Assignment item)
    {
        if (access.IsStudent && !item.IsVisibleToStudents) throw new AcademicFailure(404, "Assignment not found.");
    }

    public async Task<AssignmentResponse> CreateAsync(CreateAssignmentRequest request, CancellationToken ct)
    {
        await access.AuthorizeAsync(request.ClassId, ClassAccess.Manage, ct);
        var userId = access.UserId();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > Assignment.TitleMaxLength ||
            (request.Description?.Length ?? 0) > Assignment.DescriptionMaxLength ||
            request.OpensAtUtc == default || request.ClosesAtUtc == default || request.OpensAtUtc >= request.ClosesAtUtc)
            throw new AcademicFailure(400, "A title (maximum 200 characters), valid opening and closing times are required.");
        var kind = ParseKind(request.Kind) ?? AssignmentKind.Homework;
        var (unit, lesson) = await SessionDefaultsAsync(request.ClassId, request.SessionId, request.Unit, request.Lesson, ct);
        CheckLabels(unit, lesson);
        var now = clock.GetUtcNow();
        var item = Assignment.Create(request.ClassId, request.Title, request.Description ?? "", request.OpensAtUtc,
            request.ClosesAtUtc, kind, unit, lesson, request.SessionId, userId, now);
        store.Add(item);
        var recipients = request.Publish == false ? -1 : await PublishCoreAsync(item, userId, now, ct);
        await store.SaveAsync(ct);
        if (recipients >= 0) await AnnounceAsync(item, recipients, ct);
        return Response(item, now);
    }

    public async Task<AssignmentResponse> UpdateAsync(Guid id, UpdateAssignmentRequest request, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        await access.AuthorizeAsync(item.ClassId, ClassAccess.Manage, ct);
        if (request.ExpectedVersion is null)
            throw new AcademicFailure(400, "expectedVersion is required.", Errors("expectedVersion", "Gửi version đang xem để chống ghi đè."));
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > Assignment.TitleMaxLength ||
            (request.Description?.Length ?? 0) > Assignment.DescriptionMaxLength ||
            request.OpensAtUtc == default || request.ClosesAtUtc == default || request.OpensAtUtc >= request.ClosesAtUtc)
            throw new AcademicFailure(400, "A title (maximum 200 characters), valid opening and closing times are required.");
        if (request.ExpectedVersion != item.Version) throw new AcademicFailure(409, "Assignment changed. Reload before editing.");
        var kind = ParseKind(request.Kind) ?? item.Kind;
        var sessionId = request.SessionId ?? item.SessionId;
        var (unit, lesson) = sessionId != item.SessionId
            ? await SessionDefaultsAsync(item.ClassId, sessionId, request.Unit, request.Lesson, ct)
            : (request.Unit, request.Lesson);
        CheckLabels(unit, lesson);
        try
        {
            item.Update(request.Title, request.Description ?? "", request.OpensAtUtc, request.ClosesAtUtc, kind, unit, lesson,
                sessionId, clock.GetUtcNow());
        }
        catch (InvalidOperationException ex) { throw new AcademicFailure(409, ex.Message); }
        await store.SaveAsync(ct);
        return Response(item, clock.GetUtcNow());
    }

    public async Task<AssignmentResponse> PublishAsync(Guid id, PublishAssignmentRequest request, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        await access.AuthorizeAsync(item.ClassId, ClassAccess.Manage, ct);
        // Retry sau khi mất response: bài đã hiển thị thì trả trạng thái hiện tại, không tạo snapshot/event mới.
        if (item.IsVisibleToStudents) return Response(item, clock.GetUtcNow());
        if (request.ExpectedVersion is { } expected && expected != item.Version)
            throw new AcademicFailure(409, "Assignment changed. Reload before publishing.");
        var now = clock.GetUtcNow();
        var recipients = await PublishCoreAsync(item, access.UserId(), now, ct);
        await store.SaveAsync(ct);
        await AnnounceAsync(item, recipients, ct);
        return Response(item, now);
    }

    // Công bố + snapshot roster active trong CÙNG transaction (Q-07). Trả số người có nghĩa vụ.
    private async Task<int> PublishCoreAsync(Assignment item, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        if (sessions.IsAvailable && item.SessionId is null)
            throw new AcademicFailure(409, "Assign a class session before publishing.");
        item.Publish(userId, now);
        var count = 0;
        foreach (var student in (await store.RosterCandidatesAsync(item.ClassId, ct)).Distinct())
        {
            if (!await access.StudentHasAccessAsync(item.ClassId, student, ct)) continue;
            store.Add(AssignmentRecipient.Create(item.Id, student, now));
            count++;
        }
        return count;
    }

    private async Task AnnounceAsync(Assignment item, int recipients, CancellationToken ct)
    {
        await events.PublishAssignmentAsync(item, recipients, ct);
        item.MarkPublishedEventSent(clock.GetUtcNow());
        try { await store.SaveAsync(ct); }
        // Dữ liệu nguồn đã commit; worker sẽ phát lại cùng EventId nếu chưa đánh dấu được.
        catch (AcademicFailure ex) { log.LogWarning(ex, "Could not mark AssignmentPublished {AssignmentId} as sent", item.Id); }
    }

    public async Task<IReadOnlyList<AssignmentResponse>> ListAsync(Guid classId, CancellationToken ct) =>
        await ListAsync(classId, new AssignmentQuery(), ct);

    public async Task<IReadOnlyList<AssignmentResponse>> ListAsync(Guid classId, AssignmentQuery query, CancellationToken ct)
    {
        await access.AuthorizeAsync(classId, ClassAccess.Read, ct);
        var status = ParseStatus(query.Status);
        var kind = ParseKind(query.Kind);
        var now = clock.GetUtcNow();
        return (await store.AssignmentsAsync(classId, ct))
            .Where(x => !access.IsStudent || x.IsVisibleToStudents)
            .Where(x => status is null || x.StatusAt(now) == status)
            .Where(x => kind is null || x.Kind == kind)
            .Where(x => CourseworkAccess.Matches(query.Search, x.Title, x.Unit, x.Lesson))
            .Select(x => Response(x, now)).ToList();
    }

    public async Task<AssignmentResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await FindAsync(id, ct);
        await access.AuthorizeAsync(item.ClassId, ClassAccess.Read, ct);
        EnsureVisible(item);
        return Response(item, clock.GetUtcNow());
    }

    public async Task<SubmissionResponse> SubmitAsync(Guid assignmentId, SubmitAssignmentRequest request, CancellationToken ct)
    {
        var userId = access.UserId();
        if (!access.IsStudent) throw new AcademicFailure(403, "Only students submit assignments.");
        var assignment = await FindAsync(assignmentId, ct);
        await access.AuthorizeAsync(assignment.ClassId, ClassAccess.Read, ct);
        EnsureVisible(assignment);
        CheckWindow(assignment);
        var ids = request.FileIds;
        if (ids is null || ids.Length < 1 || ids.Length > limits.MaxFiles || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new AcademicFailure(400, $"Select 1–{limits.MaxFiles} distinct, nonempty file IDs.");
        var confirmed = await media.GetConfirmedAsync(userId, ids, ct);
        if (confirmed is null || confirmed.Count != ids.Length || !confirmed.Select(x => x.FileId).ToHashSet().SetEquals(ids) ||
            confirmed.Any(x => x.SizeBytes is <= 0 or > 20 * 1024 * 1024) || confirmed.Sum(x => x.SizeBytes) > limits.MaxTotalBytes)
            throw new AcademicFailure(400, "Files must be confirmed, owned by you, intended for submission and within size limits.");
        var item = await store.FindSubmissionAsync(assignmentId, userId, ct);
        if (item?.GradedAt is not null) throw new AcademicFailure(409, "Graded submissions cannot be replaced.");
        // Set equality makes a retry safe after a lost response, irrespective of attachment ordering.
        if (item is not null && item.Files.Select(x => x.FileId).ToHashSet().SetEquals(ids)) return Response(item);
        if ((item is null && request.ExpectedRevision is not null) || (item is not null && request.ExpectedRevision != item.Revision))
            throw new AcademicFailure(409, "Submission changed. Reload before replacing its files.");
        if (item is null) { item = Submission.Create(assignmentId, userId); store.Add(item); }
        var snapshots = confirmed.Select(x => SubmissionFile.Create(item.Id, x.FileId, x.FileName, x.ContentType, x.SizeBytes)).ToList();
        // A single SaveChanges transaction persists the aggregate plus all attachments (or none).
        CheckWindow(assignment);
        item.ReplaceFiles(snapshots, clock.GetUtcNow());
        await store.SaveAsync(ct); return Response(item);
    }

    private void CheckWindow(Assignment assignment)
    {
        var now = clock.GetUtcNow();
        if (now < assignment.OpensAtUtc || now >= assignment.ClosesAtUtc) throw new AcademicFailure(409, "Assignment is not open for submission.");
    }

    public async Task<SubmissionResponse> MineAsync(Guid assignmentId, CancellationToken ct)
    {
        var id = access.UserId();
        if (!access.IsStudent) throw new AcademicFailure(403, "Only students access their own submission.");
        var assignment = await FindAsync(assignmentId, ct);
        await access.AuthorizeAsync(assignment.ClassId, ClassAccess.Read, ct);
        EnsureVisible(assignment);
        return Response(await store.FindSubmissionAsync(assignmentId, id, ct) ?? throw new AcademicFailure(404, "No submission yet."));
    }

    public async Task<IReadOnlyList<SubmissionResponse>> ListSubmissionsAsync(Guid assignmentId, CancellationToken ct) =>
        await ListSubmissionsAsync(assignmentId, null, ct);

    public async Task<IReadOnlyList<SubmissionResponse>> ListSubmissionsAsync(Guid assignmentId, bool? graded, CancellationToken ct)
    {
        var assignment = await FindAsync(assignmentId, ct);
        await access.AuthorizeAsync(assignment.ClassId, ClassAccess.Manage, ct);
        return (await store.SubmissionsAsync(assignmentId, ct))
            .Where(x => graded is null || (x.GradeVersion > 0) == graded)
            .Select(Response).ToList();
    }

    // US-19/20: trạng thái nộp của từng học viên trong snapshot công bố (và cả người nộp dù vào lớp sau).
    public async Task<IReadOnlyList<RecipientResponse>> RecipientsAsync(Guid assignmentId, CancellationToken ct)
    {
        var assignment = await FindAsync(assignmentId, ct);
        await access.AuthorizeAsync(assignment.ClassId, ClassAccess.Manage, ct);
        var submissions = (await store.SubmissionsAsync(assignmentId, ct)).ToDictionary(x => x.StudentId);
        var recipients = await store.RecipientsAsync(assignmentId, ct);
        var result = recipients.Select(x =>
        {
            submissions.TryGetValue(x.StudentId, out var s);
            return new RecipientResponse(x.StudentId, x.AssignedAt, x.Outcome.ToString(), SubmissionStatus(s, x), s?.Id,
                s?.SubmittedAt, s?.Score, s?.GradeVersion ?? 0);
        }).ToList();
        var obligated = recipients.Select(x => x.StudentId).ToHashSet();
        result.AddRange(submissions.Values.Where(x => !obligated.Contains(x.StudentId)).Select(s =>
            new RecipientResponse(s.StudentId, s.SubmittedAt, "NotObligated", SubmissionStatus(s, null), s.Id, s.SubmittedAt,
                s.Score, s.GradeVersion)));
        return result.OrderBy(x => x.StudentId).ToList();
    }

    // US-22: học viên xem bài/điểm của mình qua mọi lớp còn quyền, có lọc/tìm kiếm/phân trang.
    public async Task<PageResponse<StudentAssignmentResponse>> StudentAssignmentsAsync(StudentAssignmentQuery query, CancellationToken ct)
    {
        var userId = await access.EnsureActiveAsync(ct);
        if (!access.IsStudent) throw new AcademicFailure(403, "Only students list their own assignments.");
        var status = ParseStatus(query.Status);
        if (status == AssignmentStatus.Draft) throw new AcademicFailure(400, "Students cannot filter draft assignments.");
        var kind = ParseKind(query.Kind);
        var submissionStatus = ParseSubmissionStatus(query.SubmissionStatus);
        List<Guid> classIds;
        if (query.ClassId is { } classId)
        {
            await access.AuthorizeAsync(classId, ClassAccess.Read, ct);
            classIds = [classId];
        }
        else
        {
            classIds = [];
            foreach (var candidate in (await store.StudentClassCandidatesAsync(userId, ct)).Distinct())
                if (await access.StudentHasAccessAsync(candidate, userId, ct)) classIds.Add(candidate);
        }
        var now = clock.GetUtcNow();
        var assignments = (await store.ClassAssignmentsAsync(classIds, ct)).Where(x => x.IsVisibleToStudents).ToList();
        var ids = assignments.Select(x => x.Id).ToList();
        var submissions = (await store.StudentSubmissionsAsync(userId, ids, ct)).ToDictionary(x => x.AssignmentId);
        var obligations = (await store.StudentObligationsAsync(userId, ct)).ToDictionary(x => x.AssignmentId);
        var items = assignments.Select(a =>
            {
                submissions.TryGetValue(a.Id, out var s);
                obligations.TryGetValue(a.Id, out var o);
                return (Assignment: a, Status: a.StatusAt(now), Response: new StudentAssignmentResponse(Response(a, now),
                    SubmissionStatus(s, o), o is not null, s?.Id, s?.SubmittedAt, s?.Score, s?.Feedback, s?.GradedAt));
            })
            .Where(x => status is null || x.Status == status)
            .Where(x => kind is null || x.Assignment.Kind == kind)
            .Where(x => submissionStatus is null || x.Response.SubmissionStatus == submissionStatus)
            .Where(x => CourseworkAccess.Matches(query.Search, x.Assignment.Title, x.Assignment.Unit, x.Assignment.Lesson))
            // Bài còn mở/sắp mở theo hạn gần nhất trước, sau đó bài đã đóng mới nhất trước.
            .OrderBy(x => x.Status == AssignmentStatus.Closed)
            .ThenBy(x => x.Status == AssignmentStatus.Closed ? -x.Assignment.ClosesAtUtc.UtcTicks : x.Assignment.ClosesAtUtc.UtcTicks)
            .ThenBy(x => x.Assignment.Title).ThenBy(x => x.Assignment.Id)
            .Select(x => x.Response).ToList();
        return CourseworkAccess.Page(items, query.Page, query.PageSize);
    }

    public async Task<FileDownloadResponse> DownloadAsync(Guid submissionId, Guid fileId, CancellationToken ct)
    {
        var id = access.UserId();
        var item = await store.FindSubmissionByIdAsync(submissionId, ct) ?? throw new AcademicFailure(404, "Submission not found.");
        var assignment = await FindAsync(item.AssignmentId, ct);
        if (access.IsStudent && item.StudentId != id) throw new AcademicFailure(403, "This is another student's submission.");
        await access.AuthorizeAsync(assignment.ClassId, access.IsStudent ? ClassAccess.Read : ClassAccess.Manage, ct);
        if (item.Files.All(x => x.FileId != fileId)) throw new AcademicFailure(404, "File is not attached to this submission.");
        var download = await media.DownloadAsync(item.StudentId, fileId, ct) ?? throw new AcademicFailure(404, "Confirmed file not found.");
        return new(download.Url, download.ExpiresAt);
    }

    /// <summary>
    /// Staff/Admin phát lại event của một bài từ dữ liệu đã lưu (cùng EventId/khóa nghiệp vụ) để đối soát consumer
    /// sau lỗi handler/restart. Không đổi dữ liệu nguồn; có log audit người thao tác.
    /// </summary>
    public async Task<ReplayEventsResponse> ReplayEventsAsync(Guid assignmentId, CancellationToken ct)
    {
        var actor = await access.EnsureActiveAsync(ct);
        if (!access.IsManager) throw new AcademicFailure(403, "Only staff replay coursework events.");
        var item = await FindAsync(assignmentId, ct);
        int published = 0, graded = 0, missed = 0;
        var recipients = await store.RecipientsAsync(item.Id, ct);
        if (item.IsPublished)
        {
            await events.PublishAssignmentAsync(item, recipients.Count, ct);
            published = 1;
        }
        foreach (var submission in (await store.SubmissionsAsync(item.Id, ct)).Where(x => x.GradeVersion > 0))
        {
            var latest = (await store.GradeHistoryAsync(submission.Id, ct)).MaxBy(x => x.Version);
            if (latest is null) continue;
            await events.PublishGradeAsync(submission, item.ClassId, latest, ct);
            graded++;
        }
        foreach (var recipient in recipients.Where(x => x.Outcome == ObligationOutcome.Missed))
        {
            await events.PublishMissedAsync(item, recipient, ct);
            missed++;
        }
        log.LogInformation("Coursework events replayed by {ActorId} ({Role}) for assignment {AssignmentId}: published={Published}, graded={Graded}, missed={Missed}",
            actor, access.Role, item.Id, published, graded, missed);
        return new(published, graded, missed);
    }

    private async Task<(string? Unit, string? Lesson)> SessionDefaultsAsync(Guid classId, Guid? sessionId, string? unit, string? lesson,
        CancellationToken ct)
    {
        if (sessionId is null) return (unit, lesson);
        if (sessionId == Guid.Empty) throw new AcademicFailure(400, "Invalid session.", Errors("sessionId", "SessionId không hợp lệ."));
        if (!sessions.IsAvailable)
            throw new AcademicFailure(409, "Class sessions are not available yet; save the assignment without sessionId.");
        var session = await sessions.FindAsync(sessionId.Value, ct);
        if (session is null || session.ClassId != classId)
            throw new AcademicFailure(400, "Session must belong to the same class.", Errors("sessionId", "Buổi học không thuộc lớp này."));
        return (string.IsNullOrWhiteSpace(unit) ? session.Unit : unit, string.IsNullOrWhiteSpace(lesson) ? session.Lesson : lesson);
    }

    private static void CheckLabels(string? unit, string? lesson)
    {
        if (unit?.Trim().Length > Assignment.UnitMaxLength || lesson?.Trim().Length > Assignment.LessonMaxLength)
            throw new AcademicFailure(400, "Unit and lesson must not exceed 100 characters.");
    }

    private static AssignmentKind? ParseKind(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : Enum.TryParse<AssignmentKind>(value.Trim(), true, out var kind) && Enum.IsDefined(kind) && !int.TryParse(value, out _) ? kind
        : throw new AcademicFailure(400, "Kind must be InClass or Homework.", Errors("kind", "Chọn InClass (Trên lớp) hoặc Homework (Về nhà)."));

    private static AssignmentStatus? ParseStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : Enum.TryParse<AssignmentStatus>(value.Trim(), true, out var status) && Enum.IsDefined(status) && !int.TryParse(value, out _) ? status
        : throw new AcademicFailure(400, "Status must be Draft, Upcoming, Open or Closed.");

    private static string? ParseSubmissionStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : SubmissionStatuses.FirstOrDefault(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
          ?? throw new AcademicFailure(400, "Unknown submission status.");

    internal static string SubmissionStatus(Submission? submission, AssignmentRecipient? obligation) =>
        submission is { GradeVersion: > 0 } ? "Graded"
        : submission is not null ? "Submitted"
        : obligation?.Outcome == ObligationOutcome.Missed ? "Missed"
        : obligation?.Outcome == ObligationOutcome.Exempted ? "Exempted"
        : "NotSubmitted";

    internal static Dictionary<string, string[]> Errors(string field, string message) => new() { [field] = [message] };

    internal static AssignmentResponse Response(Assignment item, DateTimeOffset now) => new(item.Id, item.ClassId, item.Title,
        item.Description, item.OpensAtUtc, item.ClosesAtUtc, item.Kind.ToString(), item.Unit, item.Lesson, item.SessionId,
        item.IsLegacy, item.StatusAt(now).ToString(), item.PublishedAt, item.CreatedAt, item.Version);

    internal static SubmissionResponse Response(Submission item) => new(item.Id, item.AssignmentId, item.StudentId, item.Revision,
        item.SubmittedAt,
        item.Files.OrderBy(x => x.FileName).ThenBy(x => x.FileId).Select(x => new SubmissionFileResponse(x.FileId, x.FileName, x.ContentType, x.SizeBytes)).ToList(),
        item.Score, item.Feedback, item.GradeVersion, item.GradedAt, item.GradedBy);
}
