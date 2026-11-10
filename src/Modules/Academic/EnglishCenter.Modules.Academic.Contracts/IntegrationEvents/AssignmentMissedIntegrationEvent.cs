using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

// Academic phát sau hạn nộp cho học viên không có bài nộp hợp lệ; chống trùng theo AssignmentId/StudentId.
public sealed record AssignmentMissedIntegrationEvent(Guid AssignmentId, Guid ClassId, Guid StudentId) : IntegrationEvent;
