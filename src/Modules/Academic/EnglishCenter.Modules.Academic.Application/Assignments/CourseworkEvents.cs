using System.Security.Cryptography;
using System.Text;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Assignments;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.Assignments;

/// <summary>
/// Phát event bài tập SAU khi dữ liệu nguồn đã commit. EventId sinh cố định từ khóa nghiệp vụ nên phát lại
/// (retry worker hoặc replay của Staff/Admin) giữ nguyên khóa; consumer vẫn phải dedupe theo khóa nghiệp vụ.
/// Event bus in-memory không bền vững: dữ liệu để dựng lại event nằm trong bảng nguồn (published_at, grade history, recipients).
/// Luôn phát ở chế độ strict: handler nào lỗi thì bus ném <see cref="AggregateException"/> (sau khi đã gọi hết handler),
/// người gọi KHÔNG được đánh dấu đã gửi để worker phát lại cùng EventId khi consumer phục hồi.
/// </summary>
public sealed class CourseworkEvents(IEventBus bus)
{
    /// <summary>
    /// Chờ một lần phát; trả false (đã log) nếu có handler lỗi để người gọi giữ event ở trạng thái chưa gửi.
    /// Lỗi khác (hủy, lỗi hạ tầng) vẫn được ném lên như cũ.
    /// </summary>
    public static async Task<bool> DeliveredAsync(Task publish, ILogger log, string eventName, Guid key)
    {
        try
        {
            await publish;
            return true;
        }
        catch (AggregateException ex)
        {
            log.LogWarning(ex, "{Event} {Key} was not delivered to every handler; it stays pending for retry", eventName, key);
            return false;
        }
    }

    public Task PublishAssignmentAsync(Assignment item, int recipients, CancellationToken ct) =>
        bus.PublishAsync(new AssignmentPublishedIntegrationEvent(item.Id, item.ClassId, item.Title,
            item.OpensAtUtc.UtcDateTime, item.ClosesAtUtc.UtcDateTime)
        {
            EventId = Key("assignment-published", item.Id),
            OccurredOnUtc = item.PublishedAt!.Value.UtcDateTime,
            PublishedAtUtc = item.PublishedAt.Value.UtcDateTime,
            Kind = item.Kind.ToString(),
            RecipientCount = recipients
        }, ct, throwOnHandlerFailure: true);

    public Task PublishGradeAsync(Submission submission, Guid classId, SubmissionGrade grade, CancellationToken ct) =>
        bus.PublishAsync(new SubmissionGradedIntegrationEvent(submission.Id, submission.AssignmentId, submission.StudentId, grade.Score)
        {
            EventId = Key("submission-graded", submission.Id, grade.Version),
            OccurredOnUtc = grade.GradedAt.UtcDateTime,
            ClassId = classId,
            GradeVersion = grade.Version,
            GradedAtUtc = grade.GradedAt.UtcDateTime
        }, ct, throwOnHandlerFailure: true);

    public Task PublishMissedAsync(Assignment item, AssignmentRecipient recipient, CancellationToken ct) =>
        bus.PublishAsync(new AssignmentMissedIntegrationEvent(item.Id, item.ClassId, recipient.StudentId)
        {
            EventId = Key("assignment-missed", item.Id, recipient.StudentId),
            // TV3 dùng OccurredOnUtc làm thời điểm vi phạm: là hạn nộp, không phải lúc worker chạy.
            OccurredOnUtc = item.ClosesAtUtc.UtcDateTime,
            DueAtUtc = item.ClosesAtUtc.UtcDateTime,
            PublishedAtUtc = item.PublishedAt!.Value.UtcDateTime
        }, ct, throwOnHandlerFailure: true);

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
