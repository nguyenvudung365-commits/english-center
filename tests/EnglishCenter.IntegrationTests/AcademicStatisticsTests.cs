using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class AcademicStatisticsTests : IAsyncLifetime
{
    private readonly AttendanceApiFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Theory]
    [InlineData("2027-01-01T10:59:59.9999999Z", 0)]
    [InlineData("2027-01-01T11:00:00Z", 0)]
    [InlineData("2027-01-01T11:00:00.0000001Z", 1)]
    public async Task ProgressCompletionIsStrictlyAfterRawEnd(string instant, int completed)
    {
        fixture.Host.Clock.UtcNow = DateTimeOffset.Parse(instant);
        await fixture.AsAsync("student");
        var progress = await fixture.ReadAsync($"/api/academic/classes/{fixture.Host.Second}/progress");
        Assert.Equal(1, progress.GetProperty("totalSessions").GetInt32());
        Assert.Equal(completed, progress.GetProperty("completedSessions").GetInt32());
        Assert.Equal(1 - completed, progress.GetProperty("remainingSessions").GetInt32());
        Assert.Equal(completed * 100, progress.GetProperty("completionPercent").GetInt32());
    }

    [Fact]
    public async Task MissingProvidersAreHonestPartialAndChartHasExactlyTwelveOrderedMonths()
    {
        await fixture.AsAsync("student");
        var stats = await fixture.ReadAsync("/api/academic/statistics/mine?month=2027-01");
        Assert.Equal("2027-01", stats.GetProperty("month").GetString());
        Assert.Equal("UTC+07:00", stats.GetProperty("timezone").GetString());
        Assert.Equal("partial", stats.GetProperty("availability").GetString());
        var contributors = stats.GetProperty("contributors").EnumerateArray().ToArray();
        Assert.Equal(new[] { "assessment", "warnings", "feedback" }, contributors.Select(x => x.GetProperty("key").GetString()));
        Assert.All(contributors, section =>
        {
            Assert.Equal("unavailable", section.GetProperty("availability").GetString());
            Assert.Equal("not_registered", section.GetProperty("reason").GetString());
            Assert.Equal(JsonValueKind.Null, section.GetProperty("metrics").ValueKind);
        });
        var academic = stats.GetProperty("academic");
        Assert.Equal(1, academic.GetProperty("currentClassCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, academic.GetProperty("completedSessionsChangePercent").ValueKind);
        Assert.Equal(0, academic.GetProperty("attendance").GetProperty("recordedCount").GetInt32());
        var chart = stats.GetProperty("chart").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(1, 12).Select(m => $"2027-{m:D2}"), chart.Select(p => p.GetProperty("month").GetString()));
        Assert.All(chart, p => Assert.Equal(0, p.GetProperty("recordedAttendance").GetInt32()));
    }

    [Fact]
    public async Task StatisticsBucketsSessionStartsAtVietnamMidnightNotUtcMidnight()
    {
        await fixture.AddSessionAsync("2026-12-31T16:30:00Z", "2026-12-31T16:45:00Z");
        await fixture.AddSessionAsync("2026-12-31T17:00:00Z", "2026-12-31T17:15:00Z");
        fixture.Host.Clock.UtcNow = DateTimeOffset.Parse("2027-01-01T12:00:00Z");
        await fixture.AsAsync("student");
        var stats = await fixture.ReadAsync("/api/academic/statistics/mine?month=2027-01");
        var academic = stats.GetProperty("academic");
        Assert.Equal(2, academic.GetProperty("currentCompletedSessions").GetInt32());
        Assert.Equal(1, academic.GetProperty("previousCompletedSessions").GetInt32());
        Assert.Equal(100, academic.GetProperty("completedSessionsChangePercent").GetDecimal());
        var points = stats.GetProperty("chart").EnumerateArray().ToArray();
        Assert.Equal(2, points[0].GetProperty("completedSessions").GetInt32());
        Assert.All(points.Skip(1), p => Assert.Equal(0, p.GetProperty("completedSessions").GetInt32()));
    }

    [Fact]
    public async Task RevokedMembershipExcludesClassCompletionButRetainsOnlyOwnRecordedAttendance()
    {
        using var own = await fixture.PutAsync("Present", null);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        await fixture.AsAsync("staff");
        using var created = await fixture.Client.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl,
            fixture.Host.Body(fixture.Host.Other));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var peerSession = (await AcademicScheduleFixture.JsonAsync(created)).GetProperty("id").GetGuid();
        await fixture.AsAsync("other-teacher");
        using var peer = await fixture.Client.PutAsJsonAsync(
            $"/api/academic/sessions/{peerSession}/attendance/{fixture.Host.Users["other-student"]}",
            new { status = "AbsentUnexcused", expectedRevision = (int?)null });
        Assert.Equal(HttpStatusCode.OK, peer.StatusCode);

        fixture.Host.Clock.UtcNow = DateTimeOffset.Parse("2027-01-01T12:00:00Z");
        await fixture.Host.RevokeAsync();
        await fixture.AsAsync("student");
        var stats = await fixture.ReadAsync("/api/academic/statistics/mine?month=2027-01");
        var academic = stats.GetProperty("academic");
        Assert.Equal(0, academic.GetProperty("currentClassCount").GetInt32());
        Assert.Equal(0, academic.GetProperty("currentCompletedSessions").GetInt32());
        Assert.Equal(0, academic.GetProperty("previousCompletedSessions").GetInt32());
        var attendance = academic.GetProperty("attendance");
        Assert.Equal(1, attendance.GetProperty("recordedCount").GetInt32());
        Assert.Equal(0, attendance.GetProperty("absentCount").GetInt32());
        Assert.Equal(0, attendance.GetProperty("lateCount").GetInt32());
        Assert.Equal(0, attendance.GetProperty("leftEarlyCount").GetInt32());
        var chart = stats.GetProperty("chart").EnumerateArray().ToArray();
        Assert.Equal(12, chart.Length);
        Assert.Equal("2027-01", chart[0].GetProperty("month").GetString());
        Assert.Equal(1, chart[0].GetProperty("recordedAttendance").GetInt32());
        Assert.All(chart, point => Assert.Equal(0, point.GetProperty("completedSessions").GetInt32()));
        Assert.All(chart.Skip(1), point => Assert.Equal(0, point.GetProperty("recordedAttendance").GetInt32()));
        foreach (var key in new[] { "present", "absentUnexcused", "absentExcused", "late", "lateOver20Min", "leftEarly" })
        {
            var expected = key == "present" ? 1 : 0;
            Assert.Equal(expected, attendance.GetProperty("counts").GetProperty(key).GetInt32());
            Assert.Equal(expected, chart[0].GetProperty("counts").GetProperty(key).GetInt32());
        }
    }

    [Theory]
    [InlineData("0001-12")]
    [InlineData("9999-01")]
    [InlineData("2027-13")]
    [InlineData("2027-1")]
    [InlineData("not-a-month")]
    public async Task InvalidMonthsAreBadRequest(string month)
    {
        await fixture.AsAsync("student");
        using var response = await fixture.Client.GetAsync("/api/academic/statistics/mine?month=" + Uri.EscapeDataString(month));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
