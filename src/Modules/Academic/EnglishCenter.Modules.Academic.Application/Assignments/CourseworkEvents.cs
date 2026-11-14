using System.Security.Cryptography;
using System.Text;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Assignments;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

/// <summary>
/// Phát event bài tập SAU khi dữ liệu nguồn đã commit. EventId sinh cố định từ khóa nghiệp vụ nên phát lại
/// (retry worker hoặc replay của Staff/Admin) giữ nguyên khóa; consumer vẫn phải dedupe theo khóa nghiệp vụ.
/// Event bus in-memory không bền vững: dữ liệu để dựng lại event nằm trong bảng nguồn (published_at, grade history, recipients).
/// </summary>
public sealed class CourseworkEvents(IEventBus bus)
{
    public Task PublishAssignmentAsync(Assignment item, int recipients, CancellationToken ct) =>
        bus.PublishAsync(new AssignmentPublishedIntegrationEvent(item.Id, item.ClassId, item.Title,
            item.OpensAtUtc.UtcDateTime, item.ClosesAtUtc.UtcDateTime)
        {
            EventId = Key("assignment-published", item.Id),
            OccurredOnUtc = item.PublishedAt!.Value.UtcDateTime,
            PublishedAtUtc = item.PublishedAt.Value.UtcDateTime,
            Kind = item.Kind.ToString(),
            RecipientCount = recipients
        }, ct);

    public Task PublishGradeAsync(Submission submission, Guid classId, SubmissionGrade grade, CancellationToken ct) =>
        bus.PublishAsync(new SubmissionGradedIntegrationEvent(submission.Id, submission.AssignmentId, submission.StudentId, grade.Score)
        {
            EventId = Key("submission-graded", submission.Id, grade.Version),
            OccurredOnUtc = grade.GradedAt.UtcDateTime,
            ClassId = classId,
            GradeVersion = grade.Version,
            GradedAtUtc = grade.GradedAt.UtcDateTime
        }, ct);

    public Task PublishMissedAsync(Assignment item, AssignmentRecipient recipient, CancellationToken ct) =>
        bus.PublishAsync(new AssignmentMissedIntegrationEvent(item.Id, item.ClassId, recipient.StudentId)
        {
            EventId = Key("assignment-missed", item.Id, recipient.StudentId),
            // TV3 dùng OccurredOnUtc làm thời điểm vi phạm: là hạn nộp, không phải lúc worker chạy.
            OccurredOnUtc = item.ClosesAtUtc.UtcDateTime,
            DueAtUtc = item.ClosesAtUtc.UtcDateTime,
            PublishedAtUtc = item.PublishedAt!.Value.UtcDateTime
        }, ct);

    // Guid tất định (dạng UUID v5 trên SHA-256) từ loại event + khóa nghiệp vụ.
    public static Guid Key(string type, params object[] parts)
    {
        var text = "academic:" + type + ":" + string.Join(':', parts.Select(x => x.ToString()));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
