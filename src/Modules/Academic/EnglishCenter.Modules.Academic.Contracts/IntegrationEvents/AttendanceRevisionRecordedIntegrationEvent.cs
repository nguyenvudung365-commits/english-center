using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

public sealed record AttendanceRevisionRecordedIntegrationEvent(Guid AttendanceId, Guid SessionId, Guid ClassId,
    Guid StudentId, string Status, int Revision, Guid RecordedBy, DateTime RecordedAtUtc) : IntegrationEvent;
