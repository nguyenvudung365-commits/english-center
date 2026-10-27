using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

public sealed record ClassCreatedIntegrationEvent(Guid ClassId, string ClassCode, DateOnly StartDate, DateOnly EndDate, int MaxStudents, decimal TuitionFee, string ClassType) : IntegrationEvent;
