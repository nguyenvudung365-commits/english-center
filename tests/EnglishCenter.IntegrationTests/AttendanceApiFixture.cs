using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Composition preserves the schedule fixture's intentionally stale membership projections.
internal sealed class AttendanceApiFixture : IAsyncDisposable
{
    internal AcademicScheduleFixture Host { get; } = new();
    internal HttpClient Client => Host.Client;
    internal Guid Student => Host.Users["student"];
    internal Guid Session { get; private set; }
    internal string WriteUrl => $"/api/academic/sessions/{Session}/attendance/{Student}";
    internal string RosterUrl => $"/api/academic/sessions/{Session}/attendance";

    internal async Task InitializeAsync()
    {
        await Host.InitializeAsync();
        Session = await AddSessionAsync();
        await AsAsync("teacher");
        Host.Clock.UtcNow = DateTimeOffset.Parse("2027-01-01T10:30:00Z");
    }

    internal async Task<Guid> AddSessionAsync(string start = "2027-01-01T10:00:00Z",
        string end = "2027-01-01T11:00:00Z")
    {
        await AsAsync("staff");
        using var response = await Client.PostAsJsonAsync(AcademicScheduleFixture.SessionsUrl,
            Host.Body(Host.Second, start, end));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await AcademicScheduleFixture.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    internal Task<HttpResponseMessage> PutAsync(string status, int? revision) =>
        Client.PutAsJsonAsync(WriteUrl, new { status, expectedRevision = revision });

    internal async Task AsAsync(string actor)
    {
        // JWT validation uses wall time; freeze only the business clock, not token issuance.
        var instant = Host.Clock.UtcNow;
        Host.Clock.UtcNow = null;
        try { await Host.AsAsync(actor); }
        finally { Host.Clock.UtcNow = instant; }
    }

    internal async Task<JsonElement> ReadAsync(string url)
    {
        using var response = await Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AcademicScheduleFixture.JsonAsync(response);
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}
