using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

public sealed record SubmissionGradedIntegrationEvent(Guid SubmissionId, Guid AssignmentId, Guid StudentId, decimal Score) : IntegrationEvent;
