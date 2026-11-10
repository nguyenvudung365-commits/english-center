using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

public sealed record WarningIssuedIntegrationEvent(Guid WarningId, Guid EnrollmentId, Guid StudentId, Guid ClassId,
    string WarningType) : IntegrationEvent;
