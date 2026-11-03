using System.Text.Json;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class Tv3Week2ContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("Main", 0)]
    [InlineData("Supplementary", 4_500_000)]
    public void ClassCreated_JsonPreservesEnrollmentSnapshotPayload(string type, int fee)
    {
        var source = new ClassCreatedIntegrationEvent(Guid.NewGuid(), "TV3-A", new DateOnly(2027, 1, 1),
            new DateOnly(2027, 3, 1), 20, fee, type);
        var json = JsonSerializer.Serialize(source, Json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "classCode", "classId", "classType", "endDate", "eventId", "maxStudents", "occurredOnUtc", "startDate", "tuitionFee" },
            document.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(source, JsonSerializer.Deserialize<ClassCreatedIntegrationEvent>(json, Json));
        Assert.Equal("2027-01-01", document.RootElement.GetProperty("startDate").GetString());
    }

    [Fact]
    public void StudentEnrolled_JsonPreservesInvoiceInputs_AndReplayIdentity()
    {
        // Qua nửa đêm: Finance phải dùng thời điểm ghi danh, không dùng lúc nhận lại event.
        var source = new StudentEnrolledIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            4_500_000m, new DateTime(2027, 1, 31, 23, 59, 0, DateTimeKind.Utc));
        var json = JsonSerializer.Serialize(source, Json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[] { "classId", "enrolledAtUtc", "enrollmentId", "eventId", "occurredOnUtc", "studentId", "tuitionFee" },
            document.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        var restored = JsonSerializer.Deserialize<StudentEnrolledIntegrationEvent>(json, Json)!;
        Assert.Equal(source, restored);
        Assert.Equal(DateTimeKind.Utc, restored.EnrolledAtUtc.Kind);
        var replay = restored with { EventId = Guid.NewGuid(), OccurredOnUtc = source.OccurredOnUtc.AddDays(2) };
        Assert.NotEqual(source.EventId, replay.EventId);
        Assert.Equal(source.EnrollmentId, replay.EnrollmentId);
        Assert.Equal(source.EnrolledAtUtc, replay.EnrolledAtUtc);
    }
}
