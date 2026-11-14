using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

// Academic phát sau hạn nộp cho học viên trong snapshot công bố không có bài nộp hợp lệ và vẫn còn quyền lớp;
// chống trùng theo AssignmentId/StudentId. OccurredOnUtc = hạn nộp; EventId cố định theo cặp khóa.
public sealed record AssignmentMissedIntegrationEvent(Guid AssignmentId, Guid ClassId, Guid StudentId) : IntegrationEvent
{
    public DateTime PublishedAtUtc { get; init; }
    public DateTime DueAtUtc { get; init; }
}
