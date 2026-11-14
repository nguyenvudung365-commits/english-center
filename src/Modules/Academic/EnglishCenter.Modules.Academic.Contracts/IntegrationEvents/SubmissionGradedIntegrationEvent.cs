using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

// Phát sau mỗi lần chấm/sửa điểm đã commit. Khóa chống trùng: SubmissionId + GradeVersion
// (EventId cũng cố định theo cặp này). Consumer chỉ nên lưu link/ID, không lưu điểm vào inbox.
public sealed record SubmissionGradedIntegrationEvent(Guid SubmissionId, Guid AssignmentId, Guid StudentId, decimal Score) : IntegrationEvent
{
    public Guid ClassId { get; init; }
    public int GradeVersion { get; init; }
    public DateTime GradedAtUtc { get; init; }
}
