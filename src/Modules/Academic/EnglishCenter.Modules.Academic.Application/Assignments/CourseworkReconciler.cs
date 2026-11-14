using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Enrollment.Contracts;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

public sealed record ReconcileResult(int PublishedRetried, int GradesRetried, int AssignmentsResolved, int Missed, int Exempted, int MissedSent);

/// <summary>
/// Việc chạy định kỳ (worker) hoặc gọi tay trong test/đối soát:
/// 1) phát lại AssignmentPublished/SubmissionGraded đã commit nhưng chưa đánh dấu đã gửi (crash giữa commit và publish);
/// 2) nếu bật, chốt nghĩa vụ của bài đã quá hạn theo snapshot công bố (Q-07) rồi phát AssignmentMissed.
/// Không bao giờ được gọi từ GET. Bài legacy không có snapshot nên không bị suy diễn missed hồi tố.
/// </summary>
public sealed class CourseworkReconciler(IAssignmentStore store, IEnrollmentAccess enrollment, CourseworkEvents events,
    TimeProvider clock, ILogger<CourseworkReconciler> log)
{
    private const int Batch = 50;
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    public async Task<ReconcileResult> RunOnceAsync(bool resolveObligations, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Chừa thời gian để chính request vừa commit tự phát event, tránh phát đôi không cần thiết.
        var settled = now - Grace;
        var published = 0;
        foreach (var item in (await store.UnsentPublishedAsync(Batch, ct)).Where(x => x.PublishedAt <= settled))
        {
            var recipients = (await store.RecipientsAsync(item.Id, ct)).Count;
            await events.PublishAssignmentAsync(item, recipients, ct);
            item.MarkPublishedEventSent(clock.GetUtcNow());
            published++;
        }
        if (published > 0) await store.SaveAsync(ct);

        var grades = 0;
        foreach (var grade in (await store.UnsentGradesAsync(Batch, ct)).Where(x => x.GradedAt <= settled))
        {
            var submission = await store.FindSubmissionByIdAsync(grade.SubmissionId, ct);
            var assignment = submission is null ? null : await store.FindAssignmentAsync(submission.AssignmentId, ct);
            if (submission is null || assignment is null) continue;
            await events.PublishGradeAsync(submission, assignment.ClassId, grade, ct);
            grade.MarkEventSent(clock.GetUtcNow());
            grades++;
        }
        if (grades > 0) await store.SaveAsync(ct);

        int resolved = 0, missed = 0, exempted = 0;
        if (resolveObligations)
        {
            // Chờ thêm Grace sau hạn để bài nộp sát giờ (đã qua kiểm tra hạn) kịp commit trước khi chốt.
            foreach (var item in await store.DueUnresolvedAsync(settled, Batch, ct))
            {
                var (m, e) = await ResolveAsync(item, now, ct);
                missed += m; exempted += e; resolved++;
            }
        }

        var sent = 0;
        var cache = new Dictionary<Guid, Assignment?>();
        foreach (var recipient in await store.UnsentMissedAsync(Batch, ct))
        {
            if (!cache.TryGetValue(recipient.AssignmentId, out var item))
                cache[recipient.AssignmentId] = item = await store.FindAssignmentAsync(recipient.AssignmentId, ct);
            if (item is null) continue;
            await events.PublishMissedAsync(item, recipient, ct);
            recipient.MarkMissedEventSent(clock.GetUtcNow());
            sent++;
        }
        if (sent > 0) await store.SaveAsync(ct);

        var result = new ReconcileResult(published, grades, resolved, missed, exempted, sent);
        if (published + grades + resolved + sent > 0) log.LogInformation("Coursework reconciliation: {@Result}", result);
        return result;
    }

    // Chốt toàn bộ nghĩa vụ của một bài trong một transaction; trạng thái cuối không bị mở lại.
    private async Task<(int Missed, int Exempted)> ResolveAsync(Assignment item, DateTimeOffset now, CancellationToken ct)
    {
        var submissions = (await store.SubmissionsAsync(item.Id, ct)).ToDictionary(x => x.StudentId);
        int missed = 0, exempted = 0;
        foreach (var recipient in (await store.RecipientsAsync(item.Id, ct)).Where(x => x.Outcome == ObligationOutcome.Pending))
        {
            if (submissions.TryGetValue(recipient.StudentId, out var s) && s.SubmittedAt < item.ClosesAtUtc)
                recipient.Resolve(ObligationOutcome.Submitted, now);
            // Quyền lớp đã bị thu hồi khi chốt → miễn. Enrollment chưa có lịch sử thời điểm thu hồi, nên trường hợp
            // thu hồi giữa hạn nộp và lúc worker chạy cũng được miễn (thiên về không phạt sai).
            else if (!await enrollment.IsActiveAsync(item.ClassId, recipient.StudentId, ct))
            { recipient.Resolve(ObligationOutcome.Exempted, now); exempted++; }
            else { recipient.Resolve(ObligationOutcome.Missed, now); missed++; }
        }
        item.MarkObligationsResolved(now);
        await store.SaveAsync(ct);
        return (missed, exempted);
    }
}
