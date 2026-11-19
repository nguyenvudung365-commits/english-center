using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class AcademicAttendanceTests : IAsyncLifetime
{
    private readonly AttendanceApiFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task SixStatuses_CreateCorrectionsNoOpAndStaleRetryKeepImmutableHistory()
    {
        string[] statuses = ["Present", "AbsentUnexcused", "AbsentExcused", "Late", "LateOver20Min", "LeftEarly"];
        JsonElement first = default;
        for (var i = 0; i < statuses.Length; i++)
        {
            using var response = await fixture.PutAsync(statuses[i], i == 0 ? null : i);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var row = await AcademicScheduleFixture.JsonAsync(response);
            if (i == 0) first = row;
            Assert.Equal(i + 1, row.GetProperty("revision").GetInt32());
            Assert.Equal(statuses[i], row.GetProperty("status").GetString());
            Assert.Equal(first.GetProperty("id").GetGuid(), row.GetProperty("id").GetGuid());
            Assert.Equal(first.GetProperty("recordedAtUtc").GetString(), row.GetProperty("recordedAtUtc").GetString());
            Assert.Equal(fixture.Host.Users["teacher"], row.GetProperty("recordedBy").GetGuid());
        }
        using var unchanged = await fixture.PutAsync("LeftEarly", 6);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(6, (await AcademicScheduleFixture.JsonAsync(unchanged)).GetProperty("revision").GetInt32());
        using var stale = await fixture.PutAsync("LeftEarly", 5);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var retryCreate = await fixture.PutAsync("LeftEarly", null);
        Assert.Equal(HttpStatusCode.Conflict, retryCreate.StatusCode);
        var history = await fixture.ReadAsync($"/api/academic/attendance/{first.GetProperty("id").GetGuid()}/history");
        Assert.Equal(6, history.GetProperty("total").GetInt32());
        var rows = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, rows.Select(x => x.GetProperty("revision").GetInt32()));
        Assert.Equal(JsonValueKind.Null, rows[^1].GetProperty("previousStatus").ValueKind);
        Assert.Equal("LateOver20Min", rows[0].GetProperty("previousStatus").GetString());
        var summary = await fixture.ReadAsync($"/api/academic/attendance/summary?classId={fixture.Host.Second}");
        Assert.Equal(1, summary.GetProperty("recordedCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("leftEarlyCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("absentCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("lateCount").GetInt32());
    }

    [Fact]
    public async Task CancelledSession_KeepsAttendanceHistoryAndPreventsWrites()
    {
        using var mark = await fixture.PutAsync("Present", null);
        mark.EnsureSuccessStatusCode();
        var id = (await AcademicScheduleFixture.JsonAsync(mark)).GetProperty("id").GetGuid();
        var url = $"/api/academic/sessions/{fixture.Session}";
        await fixture.AsAsync("staff");
        var body = fixture.Host.Body(fixture.Host.Second, "2027-01-01T10:15:00Z", "2027-01-01T11:15:00Z");
        body["expectedRevision"] = 0;
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.PutAsJsonAsync(url, body)).StatusCode);
        (await fixture.Client.PostAsJsonAsync(url + "/cancel", new { expectedRevision = 0 })).EnsureSuccessStatusCode();
        await fixture.AsAsync("teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.PutAsync("Late", 1)).StatusCode);
        Assert.False((await fixture.ReadAsync(fixture.RosterUrl)).GetProperty("canWrite").GetBoolean());
        var history = await fixture.ReadAsync($"/api/academic/attendance/{id}/history");
        Assert.Equal(1, history.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task RosterUsesLiveUnprojectedEnrollmentAndMissingAttendanceIsNull()
    {
        var roster = await fixture.ReadAsync(fixture.RosterUrl);
        Assert.True(roster.GetProperty("canWrite").GetBoolean());
        Assert.Equal(1, roster.GetProperty("total").GetInt32());
        var item = Assert.Single(roster.GetProperty("items").EnumerateArray());
        Assert.Equal(fixture.Student, item.GetProperty("studentId").GetGuid());
        Assert.Equal("student", item.GetProperty("fullName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("studentCode").GetString()));
        Assert.True(item.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("attendance").ValueKind);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("staff")]
    [InlineData("other-teacher")]
    [InlineData("student")]
    public async Task OnlyAssignedTeacherMayWrite(string actor)
    {
        await fixture.AsAsync(actor);
        using var response = await fixture.PutAsync("Present", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("2027-01-01T09:59:59.9999999Z", 403)]
    [InlineData("2027-01-01T10:00:00Z", 200)]
    [InlineData("2027-01-02T11:00:00Z", 200)]
    [InlineData("2027-01-02T11:00:00.0000001Z", 403)]
    public async Task WindowUsesRawTicksAndInclusiveEndpoints(string instant, int expected)
    {
        fixture.Host.Clock.UtcNow = DateTimeOffset.Parse(instant);
        using var response = await fixture.PutAsync("Present", null);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Theory]
    [InlineData("teacher", "lock")]
    [InlineData("teacher", "role")]
    [InlineData("teacher", "delete")]
    [InlineData("student", "lock")]
    [InlineData("student", "role")]
    public async Task ExistingTokenCannotBypassLiveActorOrTargetChecks(string actor, string change)
    {
        await fixture.Host.ChangeAccountAsync(actor, change);
        using var response = await fixture.PutAsync("Present", null);
        Assert.Equal(change == "delete" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevocationRetainsOwnHistoryButNeverGrantsPeerRosterOrCurrentProgress()
    {
        using var saved = await fixture.PutAsync("Present", null);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var id = (await AcademicScheduleFixture.JsonAsync(saved)).GetProperty("id").GetGuid();
        await fixture.Host.RevokeAsync();
        using var rejected = await fixture.PutAsync("Late", 1);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        await fixture.AsAsync("student");
        var own = await fixture.ReadAsync("/api/academic/attendance");
        Assert.Equal(1, own.GetProperty("total").GetInt32());
        Assert.Equal(1, (await fixture.ReadAsync($"/api/academic/attendance/{id}/history")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await fixture.ReadAsync("/api/academic/attendance/summary")).GetProperty("recordedCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.GetAsync(fixture.RosterUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.GetAsync($"/api/academic/classes/{fixture.Host.Second}/progress")).StatusCode);
        await fixture.AsAsync("other-student");
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.GetAsync($"/api/academic/attendance/{id}/history")).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"Present\"}")]
    [InlineData("{\"status\":1,\"expectedRevision\":null}")]
    [InlineData("{\"status\":\"present\",\"expectedRevision\":null}")]
    [InlineData("{\"status\":\"Unknown\",\"expectedRevision\":null}")]
    [InlineData("{\"status\":null,\"expectedRevision\":null}")]
    [InlineData("{\"status\":\"Present\",\"expectedRevision\":0}")]
    [InlineData("{\"status\":\"Present\",\"expectedRevision\":-1}")]
    public async Task InvalidStatusOrMissingRevisionIsBadRequest(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await fixture.Client.PutAsync(fixture.WriteUrl, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
