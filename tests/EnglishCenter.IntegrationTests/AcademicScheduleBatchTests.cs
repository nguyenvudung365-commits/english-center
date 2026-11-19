using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static EnglishCenter.IntegrationTests.AcademicScheduleFixture;

namespace EnglishCenter.IntegrationTests;

// Batch scheduling over the same SQLite host as AcademicScheduleTests; class A-01/A-02 run 2026-12-31..2027-01-02 (UTC+7).
public sealed class AcademicScheduleBatchTests : IAsyncLifetime
{
    private const string PreviewUrl = SessionsUrl + "/batch/preview";
    private const string BatchUrl = SessionsUrl + "/batch";
    private readonly AcademicScheduleFixture fixture = new();
    private HttpClient Client => fixture.Client;
    public Task InitializeAsync() => fixture.InitializeAsync();
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private Dictionary<string, object?> Batch(Guid? classId, params (string Start, string End)[] slots) => new()
    {
        ["classId"] = classId ?? fixture.First, ["unit"] = " Unit 1 ", ["lesson"] = " Bài học ",
        ["skills"] = new[] { "Speaking", "Listening" }, ["sessionType"] = "Regular", ["examSlot"] = " ",
        ["slots"] = slots.Select(x => new { startsAtUtc = x.Start, endsAtUtc = x.End }).ToArray()
    };

    private static (string, string) Day(int day, int hour = 10) => ($"2027-01-{day:D2}T{hour:D2}:00:00Z", $"2027-01-{day:D2}T{hour + 1:D2}:00:00Z");

    private async Task<int> CountAsync() =>
        (await JsonAsync(await Client.GetAsync(Range()))).GetArrayLength();

    private static string[] Statuses(JsonElement preview) =>
        preview.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("status").GetString()!).ToArray();

    [Theory]
    [InlineData(PreviewUrl)]
    [InlineData(BatchUrl)]
    public async Task Anonymous_IsUnauthorized(string url) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync(url, Batch(null, Day(1)))).StatusCode);

    [Theory]
    [InlineData("teacher", PreviewUrl)]
    [InlineData("teacher", BatchUrl)]
    [InlineData("student", PreviewUrl)]
    [InlineData("student", BatchUrl)]
    public async Task NonManagers_AreForbidden_AndNothingIsWritten(string actor, string url)
    {
        await fixture.AsAsync(actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PostAsJsonAsync(url, Batch(null, Day(1)))).StatusCode);
        await fixture.AsAsync("admin");
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Preview_FlagsPersistedAcrossClassesAndIntraBatchConflicts_WithoutWriting()
    {
        await fixture.AsAsync("staff");
        (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body(fixture.Second, Day(2).Item1, Day(2).Item2))).EnsureSuccessStatusCode();
        var response = await Client.PostAsJsonAsync(PreviewUrl, Batch(fixture.First, Day(1), Day(2), ("2027-01-01T10:30:00Z", "2027-01-01T11:30:00Z"), Day(2, 12)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await JsonAsync(response);
        Assert.False(preview.GetProperty("canCreate").GetBoolean());
        Assert.Equal(["BatchOverlap", "TeacherOverlap", "BatchOverlap", "Ok"], Statuses(preview));
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Preview_AllClear_CanCreate_AndCancelledSessionsDoNotConflict()
    {
        await fixture.AsAsync("staff");
        using var created = await Client.PostAsJsonAsync(SessionsUrl, fixture.Body(fixture.First, Day(1).Item1, Day(1).Item2));
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        (await Client.PostAsJsonAsync($"{SessionsUrl}/{id}/cancel", new { expectedRevision = 0 })).EnsureSuccessStatusCode();
        var preview = await JsonAsync(await Client.PostAsJsonAsync(PreviewUrl, Batch(fixture.First, Day(1), Day(2))));
        Assert.True(preview.GetProperty("canCreate").GetBoolean());
        Assert.Equal(["Ok", "Ok"], Statuses(preview));
    }

    [Fact]
    public async Task Create_PersistsEverySlot_ReturnsFrozenSessionShape_AndIsVisibleInRange()
    {
        await fixture.AsAsync("admin");
        var response = await Client.PostAsJsonAsync(BatchUrl, Batch(fixture.First, Day(2), Day(1), Day(1, 12)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await JsonAsync(response);
        Assert.Equal(3, created.GetArrayLength());
        Assert.All(created.EnumerateArray(), x =>
        {
            Assert.Equal("Unit 1", x.GetProperty("unit").GetString());
            Assert.Equal("Bài học", x.GetProperty("lesson").GetString());
            Assert.Equal("teacher", x.GetProperty("teacherName").GetString());
            Assert.Equal(new[] { "Listening", "Speaking" }, x.GetProperty("skills").EnumerateArray().Select(s => s.GetString()));
            Assert.Equal(0, x.GetProperty("revision").GetInt32());
        });
        Assert.Equal(3, await CountAsync());
    }

    [Fact]
    public async Task Create_OnePersistedConflict_Is409_AndNoSlotIsWritten()
    {
        await fixture.AsAsync("staff");
        (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body(fixture.Second, Day(2).Item1, Day(2).Item2))).EnsureSuccessStatusCode();
        var response = await Client.PostAsJsonAsync(BatchUrl, Batch(fixture.First, Day(1), Day(2)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Slots[1]", (await JsonAsync(response)).GetProperty("errors").EnumerateObject().Select(x => x.Name));
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task Create_IntraBatchOverlap_Is409_AndNothingIsWritten()
    {
        await fixture.AsAsync("staff");
        var response = await Client.PostAsJsonAsync(BatchUrl, Batch(fixture.First, Day(1), Day(2), Day(1)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Create_SlotAfterClassWindow_Is400WithSlotKey_AndNothingIsWritten()
    {
        await fixture.AsAsync("staff");
        var response = await Client.PostAsJsonAsync(BatchUrl, Batch(fixture.First, Day(1), ("2027-02-01T10:00:00Z", "2027-02-01T11:00:00Z")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Slots[1].EndsAtUtc", (await JsonAsync(response)).GetProperty("errors").EnumerateObject().Select(x => x.Name));
        Assert.Equal(0, await CountAsync());
    }

    [Theory]
    [InlineData("2027-01-01T10:00:00", "2027-01-01T11:00:00Z")]
    [InlineData("nope", "2027-01-01T11:00:00Z")]
    public async Task Create_OffsetlessOrMalformedTimestamp_Is400(string start, string end)
    {
        await fixture.AsAsync("staff");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync(BatchUrl, Batch(fixture.First, (start, end)))).StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Theory]
    [InlineData("missing-slots")]
    [InlineData("empty-slots")]
    [InlineData("too-many")]
    public async Task Create_SlotCollectionBounds_Return400(string kind)
    {
        await fixture.AsAsync("staff");
        var body = Batch(fixture.First, Day(1));
        if (kind == "missing-slots") body.Remove("slots");
        else body["slots"] = kind == "empty-slots" ? Array.Empty<object>()
            : Enumerable.Range(0, 63).Select(i => new { startsAtUtc = $"2027-01-01T{i % 24:D2}:00:00Z", endsAtUtc = $"2027-01-01T{i % 24:D2}:30:00Z" }).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync(BatchUrl, body)).StatusCode);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task Create_MissingClass_Is404_AndTouchingSlotsAreAllowed()
    {
        await fixture.AsAsync("staff");
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsJsonAsync(BatchUrl, Batch(Guid.NewGuid(), Day(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync(BatchUrl, Batch(fixture.First, Day(1, 10), Day(1, 11)))).StatusCode);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Create_RepeatedAfterSuccess_ConflictsInsteadOfDuplicating()
    {
        await fixture.AsAsync("staff");
        var body = Batch(fixture.First, Day(1), Day(2));
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync(BatchUrl, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(BatchUrl, body)).StatusCode);
        Assert.Equal(2, await CountAsync());
    }
}
