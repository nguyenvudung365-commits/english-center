using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

public sealed record StudentEnrolledIntegrationEvent(Guid EnrollmentId, Guid StudentId, Guid ClassId, decimal TuitionFee, DateTime EnrolledAtUtc) : IntegrationEvent;
