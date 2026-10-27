using EnglishCenter.BuildingBlocks.EventBus;

namespace EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

// Status là tên trạng thái dạng string, KHÔNG dùng enum của Domain.
public sealed record AttendanceRecordedIntegrationEvent(Guid SessionId, Guid ClassId, Guid StudentId, string Status, DateTime RecordedAtUtc) : IntegrationEvent;
