using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;

public sealed record InvoicePaidIntegrationEvent(Guid InvoiceId, Guid EnrollmentId, Guid StudentId, DateTime PaidAtUtc) : IntegrationEvent;
