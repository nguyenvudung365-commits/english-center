using System.Text.Json;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class Tv3Week4ContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Present")]
    [InlineData("AbsentUnexcused")]
    [InlineData("AbsentExcused")]
    [InlineData("Late")]
    [InlineData("LateOver20Min")]
    [InlineData("LeftEarly")]
    public void AttendancePayload_RoundTrips_AndReplayKeepsRecordedAt(string status)
    {
        var source = new AttendanceRecordedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status,
            new DateTime(2026, 11, 10, 10, 30, 0, DateTimeKind.Utc));
        var json = JsonSerializer.Serialize(source, Json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "classId", "eventId", "occurredOnUtc", "recordedAtUtc", "sessionId", "status", "studentId" },
            doc.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        var restored = JsonSerializer.Deserialize<AttendanceRecordedIntegrationEvent>(json, Json)!;
        Assert.Equal(source, restored); Assert.Equal(DateTimeKind.Utc, restored.RecordedAtUtc.Kind);
        var replay = restored with { EventId = Guid.NewGuid(), OccurredOnUtc = source.OccurredOnUtc.AddDays(1) };
        Assert.Equal(source.RecordedAtUtc, replay.RecordedAtUtc);
        Assert.Equal(source.SessionId, replay.SessionId); Assert.Equal(source.StudentId, replay.StudentId);
    }

    [Fact]
    public void AssignmentMissedPayload_PreservesBusinessKeyAcrossReplay()
    {
        var source = new AssignmentMissedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var restored = JsonSerializer.Deserialize<AssignmentMissedIntegrationEvent>(JsonSerializer.Serialize(source, Json), Json)!;
        Assert.Equal(source, restored);
        var replay = restored with { EventId = Guid.NewGuid() };
        Assert.Equal(source.AssignmentId, replay.AssignmentId); Assert.Equal(source.ClassId, replay.ClassId);
        Assert.Equal(source.StudentId, replay.StudentId);
    }

    [Fact]
    public void WarningPayload_PreservesRecipientAndSourceObject()
    {
        var warning = new WarningIssuedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Absence");
        Assert.Equal(warning, JsonSerializer.Deserialize<WarningIssuedIntegrationEvent>(JsonSerializer.Serialize(warning, Json), Json));
    }

    [Fact]
    public void AssignmentPublishedPayload_PreservesClassAndSubmissionWindow()
    {
        var opens = new DateTime(2026, 11, 10, 1, 0, 0, DateTimeKind.Utc);
        var source = new AssignmentPublishedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), "Homework", opens, opens.AddDays(1));
        var restored = JsonSerializer.Deserialize<AssignmentPublishedIntegrationEvent>(JsonSerializer.Serialize(source, Json), Json)!;
        Assert.Equal(source, restored);
        Assert.Equal(DateTimeKind.Utc, restored.OpensAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, restored.ClosesAtUtc.Kind);
    }

    [Fact]
    public void SubmissionGradedPayload_PreservesStudentSubmissionAndScore()
    {
        var source = new SubmissionGradedIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7.5m);
        var restored = JsonSerializer.Deserialize<SubmissionGradedIntegrationEvent>(JsonSerializer.Serialize(source, Json), Json)!;
        Assert.Equal(source, restored);
    }
}
