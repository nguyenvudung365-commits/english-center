using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

// Phát sau khi commit công bố. EventId cố định theo AssignmentId nên replay không tạo khóa mới.
// Người nhận lấy qua IAssignmentDirectory.RecipientsAsync (snapshot roster lúc công bố, Q-07).
public sealed record AssignmentPublishedIntegrationEvent(Guid AssignmentId, Guid ClassId, string Title, DateTime OpensAtUtc, DateTime ClosesAtUtc) : IntegrationEvent
{
    public DateTime PublishedAtUtc { get; init; }
    public string Kind { get; init; } = string.Empty;
    public int RecipientCount { get; init; }
}
