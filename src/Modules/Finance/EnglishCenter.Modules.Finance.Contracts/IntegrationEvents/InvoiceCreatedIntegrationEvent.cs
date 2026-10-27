using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;

public sealed record InvoiceCreatedIntegrationEvent(Guid InvoiceId, Guid EnrollmentId, Guid StudentId, decimal Amount, DateOnly DueDate) : IntegrationEvent;
