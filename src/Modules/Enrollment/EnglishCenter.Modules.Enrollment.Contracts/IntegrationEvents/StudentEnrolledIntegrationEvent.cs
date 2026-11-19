using System.Text.Json.Serialization;
using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

public sealed record StudentEnrolledIntegrationEvent(Guid EnrollmentId, Guid StudentId, Guid ClassId, decimal TuitionFee,
    DateTime EnrolledAtUtc, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool SkipInvoice = false) : IntegrationEvent;
