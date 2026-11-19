using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static EnglishCenter.IntegrationTests.AcademicScheduleFixture;

namespace EnglishCenter.IntegrationTests;

public sealed class AcademicScheduleTests : IAsyncLifetime
{
    private readonly AcademicScheduleFixture fixture = new();
    private HttpClient Client => fixture.Client;
    public Task InitializeAsync() => fixture.InitializeAsync();
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Theory]
    [InlineData("classes")]
    [InlineData("classes/mine")]
    [InlineData("classes/00000000-0000-0000-0000-000000000001")]
    [InlineData("sessions")]
    [InlineData("sessions/00000000-0000-0000-0000-000000000001")]
    public async Task Anonymous_ReadsRequireAuthentication(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/academic/" + path)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_CannotCreateSession() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body())).StatusCode);

    [Fact]
    public async Task SessionChanges_AreAuthorizedVersionedAndReleaseCancelledSlot()
    {
        await fixture.AsAsync("staff");
        var body = fixture.Body(fixture.Second);
        var id = (await CreateAsync(body)).GetProperty("id").GetGuid();
        var url = $"{SessionsUrl}/{id}";
        body["expectedRevision"] = 0;
        body["lesson"] = "Updated lesson";
        await fixture.AsAsync("teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.PutAsJsonAsync(url, body)).StatusCode);
        await fixture.AsAsync("staff");
        (await Client.PutAsJsonAsync(url, body)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync(url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body())).StatusCode);
        (await Client.PostAsJsonAsync(url + "/cancel", new { expectedRevision = 1 })).EnsureSuccessStatusCode();
        await CreateAsync(fixture.Body());
        await fixture.AsAsync("student");
        var cancelled = await ReadAsync(url);
        Assert.Equal(id, cancelled.GetProperty("id").GetGuid());
        Assert.Equal("Updated lesson", cancelled.GetProperty("lesson").GetString());
        Assert.True(cancelled.GetProperty("isCancelled").GetBoolean());
        var notifications = await ReadAsync("/api/media-notification/notifications");
        Assert.Equal(new[] { "SessionCancelled", "SessionUpdated" }, notifications.GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("targetId").GetGuid() == id)
            .Select(x => x.GetProperty("kind").GetString()).Order().ToArray());
    }

    [Theory]
    [InlineData("admin", 3)]
    [InlineData("staff", 3)]
    [InlineData("teacher", 2)]
    [InlineData("other-teacher", 1)]
    [InlineData("student", 1)]
    [InlineData("other-student", 1)]
    public async Task ClassList_AuthorizesBeforeTotalAndPaging(string actor, int total)
    {
        await fixture.AsAsync(actor);
        var page = await ReadAsync(ClassesUrl + "?page=1&pageSize=1");
        Assert.Equal(total, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
        var expected = actor switch { "student" => fixture.Second, "other-student" or "other-teacher" => fixture.Other, _ => fixture.First };
        Assert.Equal(expected, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var next = await ReadAsync(ClassesUrl + "?page=2&pageSize=1");
        Assert.Equal(total, next.GetProperty("total").GetInt32());
        if (total == 1) Assert.Empty(next.GetProperty("items").EnumerateArray());
        else Assert.Equal(fixture.Second, Assert.Single(next.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task ClassList_DefaultPageAndSearchAreStableAndCaseInsensitive()
    {
        await fixture.AsAsync("staff");
        var all = await ReadAsync(ClassesUrl);
        Assert.Equal(1, all.GetProperty("page").GetInt32());
        Assert.Equal(20, all.GetProperty("pageSize").GetInt32());
        Assert.Equal(new[] { "A-01", "A-02", "B-01" }, all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("code").GetString()));
        var code = await ReadAsync(ClassesUrl + "?search=" + Uri.EscapeDataString(" a-02 "));
        Assert.Equal(fixture.Second, Assert.Single(code.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var unicode = await ReadAsync(ClassesUrl + "?search=" + Uri.EscapeDataString("VIỆT 🗓"));
        Assert.Equal(1, unicode.GetProperty("total").GetInt32());
        Assert.Equal(fixture.First, Assert.Single(unicode.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty((await ReadAsync(ClassesUrl + "?page=2147483647&pageSize=100")).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("2026-12-31T16:59:59Z", "Upcoming", "A-02")]
    [InlineData("2026-12-31T16:59:59Z", "Ongoing", "A-01,B-01")]
    [InlineData("2026-12-31T16:59:59Z", "Finished", "")]
    [InlineData("2026-12-31T17:00:00Z", "Upcoming", "")]
    [InlineData("2026-12-31T17:00:00Z", "Ongoing", "A-02,B-01")]
    [InlineData("2026-12-31T17:00:00Z", "Finished", "A-01")]
    public async Task ClassList_StatusUsesInclusiveVietnamDates(string instant, string status, string expectedCodes)
    {
        // The literal dates/expectations are independent of the production calendar helper.
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var items = await db.Classes.OrderBy(x => x.Code).ToListAsync();
            SetDates(items[0], new(2026, 12, 30), new(2026, 12, 31));
            SetDates(items[1], new(2027, 1, 1), new(2027, 1, 3));
            SetDates(items[2], new(2026, 12, 31), new(2027, 1, 2));
            await db.SaveChangesAsync();
            void SetDates(EnglishCenter.Modules.Academic.Domain.Classes.Class item, DateOnly start, DateOnly end)
            {
                db.Entry(item).Property(x => x.StartDate).CurrentValue = start;
                db.Entry(item).Property(x => x.EndDate).CurrentValue = end;
            }
        }
        await fixture.AsAsync("admin");
        fixture.Clock.UtcNow = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);
        var result = await ReadAsync(ClassesUrl + "?status=" + status);
        var expected = expectedCodes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected.Length, result.GetProperty("total").GetInt32());
        Assert.Equal(expected, result.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("code").GetString()));
        Assert.All(result.GetProperty("items").EnumerateArray(), x => Assert.Equal(status, x.GetProperty("status").GetString()));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=2147483648")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=0")]
    [InlineData("status=Unknown")]
    [InlineData("status=")]
    [InlineData("page=no")]
    public async Task ClassList_InvalidQueryReturns400(string query)
    {
        await fixture.AsAsync("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(ClassesUrl + "?" + query)).StatusCode);
    }

    [Fact]
    public async Task ClassList_RejectsOversizedSearch()
    {
        await fixture.AsAsync("admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(ClassesUrl + "?search=" + new string('x', 201))).StatusCode);
    }

    [Theory]
    [InlineData("teacher", true)]
    [InlineData("other-teacher", false)]
    [InlineData("student", true)]
    [InlineData("other-student", false)]
    public async Task ClassDetail_UsesCurrentAssignmentOrEnrollment(string actor, bool visible)
    {
        await fixture.AsAsync(actor);
        Assert.Equal(visible ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await Client.GetAsync($"{ClassesUrl}/{fixture.Second}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"{ClassesUrl}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Mine_KeepsFiveFieldArray_UsesLiveMembershipNotProjection_AndRechecksRevocation()
    {
        await fixture.AsAsync("student");
        var token = Client.DefaultRequestHeaders.Authorization;
        var mine = await ReadAsync(ClassesUrl + "/mine");
        Assert.Equal(JsonValueKind.Array, mine.ValueKind);
        var item = Assert.Single(mine.EnumerateArray());
        Assert.Equal(fixture.Second, item.GetProperty("classId").GetGuid());
        Assert.Equal(new[] { "classId", "code", "endDate", "name", "startDate" }, item.EnumerateObject().Select(x => x.Name).Order());
        await fixture.RevokeAsync();
        Assert.Empty((await ReadAsync(ClassesUrl + "/mine")).EnumerateArray());
        Assert.Empty((await ReadAsync(ClassesUrl)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"{ClassesUrl}/{fixture.Second}")).StatusCode);
        Assert.Equal(token, Client.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("staff")]
    [InlineData("teacher")]
    public async Task Mine_RemainsStudentOnly(string actor)
    {
        await fixture.AsAsync(actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(ClassesUrl + "/mine")).StatusCode);
    }

    [Theory]
    [InlineData("admin", "lock")]
    [InlineData("staff", "role")]
    [InlineData("teacher", "delete")]
    [InlineData("student", "lock")]
    [InlineData("student", "role")]
    public async Task StaleToken_ClassReadsRejectBeforeResourceLookup(string actor, string change)
    {
        await fixture.AsAsync(actor);
        await fixture.ChangeAccountAsync(actor, change);
        var expected = change == "delete" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        foreach (var path in new[] { ClassesUrl, $"{ClassesUrl}/{fixture.Second}", $"{ClassesUrl}/{Guid.NewGuid()}" })
            Assert.Equal(expected, (await Client.GetAsync(path)).StatusCode);
        if (actor == "student") Assert.Equal(expected, (await Client.GetAsync(ClassesUrl + "/mine")).StatusCode);
    }

    [Theory]
    [InlineData("courses", "lock")]
    [InlineData("teachers", "role")]
    [InlineData("create", "delete")]
    [InlineData("replay", "lock")]
    [InlineData("republish", "role")]
    public async Task RetainedManagerEndpoints_CheckLiveAccountBeforeReadsOrMutations(string operation, string change)
    {
        await fixture.AsAsync("staff");
        await fixture.ChangeAccountAsync("staff", change);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var original = await db.Classes.CountAsync();
        var response = operation switch
        {
            "courses" or "teachers" => await Client.GetAsync("/api/academic/" + operation),
            "replay" => await Client.PostAsync($"{ClassesUrl}/{fixture.First}/replay", null),
            "republish" => await Client.PostAsync(ClassesUrl + "/republish", null),
            _ => await Client.PostAsJsonAsync(ClassesUrl, new { code = "UNAUTHORIZED", name = "Synthetic", courseId = (await db.Courses.SingleAsync(x => x.Code == "SCHEDULE")).Id,
                teacherId = fixture.Users["teacher"], classType = "Main", startDate = "2026-12-31", endDate = "2027-01-02", maxStudents = 20, tuitionFee = 0 })
        };
        Assert.Equal(change == "delete" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(original, await db.Classes.CountAsync());
    }

    [Theory]
    [InlineData("admin", HttpStatusCode.Created)]
    [InlineData("staff", HttpStatusCode.Created)]
    [InlineData("teacher", HttpStatusCode.Forbidden)]
    [InlineData("student", HttpStatusCode.Forbidden)]
    public async Task CreateSession_OnlyLiveManagersCanWrite(string actor, HttpStatusCode expected)
    {
        await fixture.AsAsync(actor);
        Assert.Equal(expected, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body())).StatusCode);
    }

    [Fact]
    public async Task CreateSession_ReturnsFrozenShapeNormalizedUtcAndCanonicalSkills_WithoutChangingTeacherSnapshot()
    {
        await fixture.AsAsync("staff");
        var before = await TeacherSnapshotAsync();
        var result = await CreateAsync(fixture.Body(start: "2027-01-01T17:00:00+07:00", end: "2027-01-01T18:00:00+07:00"));
        AssertSessionShape(result);
        Assert.Equal(fixture.First, result.GetProperty("classId").GetGuid());
        Assert.Equal(fixture.Users["teacher"], result.GetProperty("teacherId").GetGuid());
        Assert.Equal("A-01", result.GetProperty("classCode").GetString());
        Assert.Equal("Lớp Việt 🗓", result.GetProperty("className").GetString());
        Assert.Equal("teacher", result.GetProperty("teacherName").GetString());
        Assert.Equal("Unit 1", result.GetProperty("unit").GetString());
        Assert.Equal("Bài học ' Việt 🗓", result.GetProperty("lesson").GetString());
        Assert.Equal("Regular", result.GetProperty("sessionType").GetString());
        Assert.Equal(new[] { "Listening", "Speaking" }, result.GetProperty("skills").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("examSlot").ValueKind);
        Assert.Equal(DateTimeOffset.Parse("2027-01-01T10:00:00Z", CultureInfo.InvariantCulture), result.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2027-01-01T11:00:00Z", CultureInfo.InvariantCulture), result.GetProperty("endsAtUtc").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, result.GetProperty("startsAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.Zero, result.GetProperty("endsAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.Zero, result.GetProperty("createdAtUtc").GetDateTimeOffset().Offset);
        var fetched = await ReadAsync($"{SessionsUrl}/{result.GetProperty("id").GetGuid()}");
        Assert.Equal(result.GetProperty("startsAtUtc").GetString(), fetched.GetProperty("startsAtUtc").GetString());
        Assert.Equal(result.GetProperty("endsAtUtc").GetString(), fetched.GetProperty("endsAtUtc").GetString());
        Assert.Equal(before, await TeacherSnapshotAsync());
    }

    public static IEnumerable<object?[]> InvalidBodies()
    {
        foreach (var key in new[] { "classId", "startsAtUtc", "endsAtUtc", "unit", "lesson", "skills", "sessionType" })
            yield return [key, null];
        yield return ["classId", Guid.Empty];
        yield return ["classId", "not-a-guid"];
        yield return ["unit", " "];
        yield return ["unit", new string('x', 101)];
        yield return ["lesson", ""];
        yield return ["lesson", new string('x', 201)];
        yield return ["skills", Array.Empty<string>()];
        yield return ["skills", new[] { "Listening", "Listening" }];
        yield return ["skills", new[] { "Unknown" }];
        yield return ["skills", new[] { "1" }];
        yield return ["skills", "Listening"];
        yield return ["sessionType", "Unknown"];
        yield return ["sessionType", "0"];
        yield return ["sessionType", 0];
        yield return ["examSlot", "Not valid on Regular"];
        yield return ["examSlot", new string('x', 101)];
        yield return ["startsAtUtc", "2027-01-01T10:00:00"];
        yield return ["endsAtUtc", "2027-01-01T11:00:00"];
        yield return ["startsAtUtc", "not-a-time"];
        yield return ["startsAtUtc", "0001-01-01T00:00:00Z"];
        yield return ["startsAtUtc", "0001-01-01T00:00:00+14:00"];
        yield return ["endsAtUtc", "2027-01-01T10:00:00Z"];
        yield return ["endsAtUtc", "2027-01-01T09:00:00Z"];
        yield return ["unit", 123];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task CreateSession_InvalidFieldsReturnValidationProblem_AndDoNotPersist(string field, object? value)
    {
        await fixture.AsAsync("admin");
        var body = fixture.Body(); body[field] = value;
        var response = await Client.PostAsJsonAsync(SessionsUrl, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), x => x.Name.EndsWith(field, StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await ReadAsync(Range())).EnumerateArray());
    }

    [Theory]
    [InlineData("classId")]
    [InlineData("startsAtUtc")]
    [InlineData("endsAtUtc")]
    [InlineData("unit")]
    [InlineData("lesson")]
    [InlineData("skills")]
    [InlineData("sessionType")]
    public async Task CreateSession_MissingRequiredFieldReturns400(string field)
    {
        await fixture.AsAsync("staff");
        var body = fixture.Body(); body.Remove(field);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync(SessionsUrl, body)).StatusCode);
    }

    [Theory]
    [InlineData("MockTest")]
    [InlineData("FinalTest")]
    public async Task TestSession_AllowsTrimmedExamSlotAndMaximumTextLengths(string type)
    {
        await fixture.AsAsync("staff");
        var body = fixture.Body();
        body["sessionType"] = type; body["examSlot"] = " Ca thi sáng ";
        body["unit"] = new string('u', 100); body["lesson"] = new string('l', 200);
        body["skills"] = new[] { "Speaking", "Writing", "Reading", "Listening" };
        var created = await CreateAsync(body);
        Assert.Equal("Ca thi sáng", created.GetProperty("examSlot").GetString());
        Assert.Equal(type, created.GetProperty("sessionType").GetString());
        Assert.Equal(new[] { "Listening", "Reading", "Writing", "Speaking" }, created.GetProperty("skills").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("lock")]
    [InlineData("role")]
    public async Task CreateSession_RequiresExistingClassAndLiveAssignedTeacher(string kind)
    {
        await fixture.AsAsync("staff");
        if (kind != "missing") await fixture.ChangeAccountAsync("teacher", kind);
        var response = await Client.PostAsJsonAsync(SessionsUrl, fixture.Body(kind == "missing" ? Guid.NewGuid() : fixture.First));
        Assert.Equal(kind == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await ReadAsync(Range())).EnumerateArray());
    }

    [Theory]
    [InlineData("2026-12-30T17:00:00Z", "2026-12-30T18:00:00Z", HttpStatusCode.Created)]
    [InlineData("2027-01-02T16:00:00Z", "2027-01-02T17:00:00Z", HttpStatusCode.Created)]
    [InlineData("2027-01-01T23:30:00+07:00", "2027-01-02T00:30:00+07:00", HttpStatusCode.Created)]
    [InlineData("2026-12-30T16:59:59.999999Z", "2026-12-30T18:00:00Z", HttpStatusCode.BadRequest)]
    [InlineData("2027-01-02T16:00:00Z", "2027-01-02T17:00:00.000001Z", HttpStatusCode.BadRequest)]
    public async Task SessionWindow_UsesInclusiveVietnamClassDatesAndHalfOpenContainment(string start, string end, HttpStatusCode expected)
    {
        await fixture.AsAsync("staff");
        Assert.Equal(expected, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body(start: start, end: end))).StatusCode);
        Assert.Equal(expected == HttpStatusCode.Created ? 1 : 0, (await ReadAsync(Range())).GetArrayLength());
    }

    [Fact]
    public async Task ClassEndDateOverflow_IsValidationErrorNot500()
    {
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
            var item = await db.Classes.SingleAsync(x => x.Id == fixture.First);
            db.Entry(item).Property(x => x.EndDate).CurrentValue = DateOnly.MaxValue;
            await db.SaveChangesAsync();
        }
        await fixture.AsAsync("staff");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body())).StatusCode);
    }

    [Theory]
    [InlineData("2027-01-01T10:00:00Z", "2027-01-01T11:00:00Z", false, false, HttpStatusCode.Conflict)]
    [InlineData("2027-01-01T10:15:00Z", "2027-01-01T10:45:00Z", false, false, HttpStatusCode.Conflict)]
    [InlineData("2027-01-01T09:00:00Z", "2027-01-01T12:00:00Z", false, false, HttpStatusCode.Conflict)]
    [InlineData("2027-01-01T09:30:00Z", "2027-01-01T10:30:00Z", false, false, HttpStatusCode.Conflict)]
    [InlineData("2027-01-01T10:30:00Z", "2027-01-01T11:30:00Z", false, false, HttpStatusCode.Conflict)]
    [InlineData("2027-01-01T10:00:00Z", "2027-01-01T11:00:00Z", true, false, HttpStatusCode.Conflict)]
    [InlineData("2027-01-01T09:00:00Z", "2027-01-01T10:00:00Z", false, false, HttpStatusCode.Created)]
    [InlineData("2027-01-01T11:00:00Z", "2027-01-01T12:00:00Z", true, false, HttpStatusCode.Created)]
    [InlineData("2027-01-01T10:00:00Z", "2027-01-01T11:00:00Z", false, true, HttpStatusCode.Created)]
    public async Task TeacherOverlap_IsAtomicAcrossClasses_ButAllowsTouchingAndDifferentTeachers(
        string start, string end, bool sameTeacherOtherClass, bool differentTeacher, HttpStatusCode expected)
    {
        await fixture.AsAsync("staff");
        await CreateAsync(fixture.Body());
        var id = differentTeacher ? fixture.Other : sameTeacherOtherClass ? fixture.Second : fixture.First;
        Assert.Equal(expected, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body(id, start, end))).StatusCode);
        Assert.Equal(expected == HttpStatusCode.Created ? 2 : 1, (await ReadAsync(Range())).GetArrayLength());
    }

    [Theory]
    [InlineData("admin", 3)]
    [InlineData("staff", 3)]
    [InlineData("teacher", 2)]
    [InlineData("other-teacher", 1)]
    [InlineData("student", 1)]
    [InlineData("other-student", 1)]
    public async Task SessionReads_FilterByLiveRoleAndEnrollment_AndAuthorizeExplicitClass(string actor, int count)
    {
        await fixture.AsAsync("staff");
        var first = await CreateAsync(fixture.Body());
        var second = await CreateAsync(fixture.Body(fixture.Second, "2027-01-01T12:00:00Z", "2027-01-01T13:00:00Z"));
        var other = await CreateAsync(fixture.Body(fixture.Other));
        await fixture.AsAsync(actor);
        var range = await ReadAsync(Range());
        Assert.Equal(count, range.GetArrayLength());
        var createdById = new[] { first, second, other }.ToDictionary(x => x.GetProperty("id").GetGuid());
        foreach (var value in range.EnumerateArray())
        {
            AssertSessionShape(value);
            var created = createdById[value.GetProperty("id").GetGuid()];
            Assert.Equal(created.GetProperty("endsAtUtc").GetDateTimeOffset(), value.GetProperty("endsAtUtc").GetDateTimeOffset());
        }
        var visibleClasses = actor switch
        {
            "teacher" => new[] { fixture.First, fixture.Second }, "student" => new[] { fixture.Second },
            "other-student" or "other-teacher" => new[] { fixture.Other }, _ => new[] { fixture.First, fixture.Second, fixture.Other }
        };
        Assert.Equal(visibleClasses.Order(), range.EnumerateArray().Select(x => x.GetProperty("classId").GetGuid()).Order());
        var sorted = range.EnumerateArray().Select(x => (At: x.GetProperty("startsAtUtc").GetDateTimeOffset(), Id: x.GetProperty("id").GetGuid())).ToArray();
        Assert.Equal(sorted.OrderBy(x => x.At).ThenBy(x => x.Id), sorted);
        foreach (var item in new[] { first, second, other })
        {
            var allowed = visibleClasses.Contains(item.GetProperty("classId").GetGuid());
            using var detail = await Client.GetAsync($"{SessionsUrl}/{item.GetProperty("id").GetGuid()}");
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, detail.StatusCode);
            if (allowed)
            {
                var fetched = await JsonAsync(detail);
                AssertSessionShape(fetched);
                Assert.Equal(item.GetProperty("endsAtUtc").GetDateTimeOffset(), fetched.GetProperty("endsAtUtc").GetDateTimeOffset());
            }
            var response = await Client.GetAsync(Range(classId: item.GetProperty("classId").GetGuid()));
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            if (allowed)
            {
                var ranged = Assert.Single((await JsonAsync(response)).EnumerateArray());
                AssertSessionShape(ranged);
                Assert.Equal(item.GetProperty("endsAtUtc").GetDateTimeOffset(), ranged.GetProperty("endsAtUtc").GetDateTimeOffset());
            }
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync(Range(classId: Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"{SessionsUrl}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Range_HalfOpenIntersectionIncludesCrossMidnightAndExcludesTouchingBounds()
    {
        await fixture.AsAsync("admin");
        var middle = await CreateAsync(fixture.Body(start: "2027-01-01T23:30:00+07:00", end: "2027-01-02T00:30:00+07:00"));
        await CreateAsync(fixture.Body(start: "2027-01-01T15:30:00Z", end: "2027-01-01T16:30:00Z"));
        await CreateAsync(fixture.Body(start: "2027-01-01T17:30:00Z", end: "2027-01-01T18:30:00Z"));
        foreach (var (from, to) in new[] { ("2027-01-01T16:30:00Z", "2027-01-01T17:30:00Z"),
                     ("2027-01-01T23:30:00+07:00", "2027-01-02T00:00:00+07:00"),
                     ("2027-01-02T00:00:00+07:00", "2027-01-02T00:30:00+07:00") })
            Assert.Equal(middle.GetProperty("id").GetGuid(), Assert.Single((await ReadAsync(Range(from, to))).EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("fromUtc=2027-01-01T00:00:00Z")]
    [InlineData("toUtc=2027-01-02T00:00:00Z")]
    [InlineData("fromUtc=2027-01-01T00:00:00Z&toUtc=2027-01-01T00:00:00Z")]
    [InlineData("fromUtc=2027-01-02T00:00:00Z&toUtc=2027-01-01T00:00:00Z")]
    [InlineData("fromUtc=2027-01-01T00:00:00&toUtc=2027-01-02T00:00:00Z")]
    [InlineData("fromUtc=2027-01-01T00:00:00Z&toUtc=2027-01-02T00:00:00")]
    [InlineData("fromUtc=no&toUtc=2027-01-02T00:00:00Z")]
    [InlineData("fromUtc=2027-01-01T00:00:00Z&toUtc=2027-04-04T00:00:00.000001Z")]
    [InlineData("fromUtc=2027-01-01T00:00:00Z&toUtc=2027-01-02T00:00:00Z&classId=00000000-0000-0000-0000-000000000000")]
    [InlineData("fromUtc=2027-01-01T00:00:00Z&toUtc=2027-01-02T00:00:00Z&classId=no")]
    public async Task Range_InvalidQueryReturns400(string query)
    {
        await fixture.AsAsync("staff");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync(SessionsUrl + "?" + query)).StatusCode);
    }

    [Fact]
    public async Task Range_Exactly93ElapsedDaysIsValid()
    {
        await fixture.AsAsync("staff");
        Assert.Empty((await ReadAsync(Range("2027-01-01T00:00:00Z", "2027-04-04T00:00:00Z"))).EnumerateArray());
    }

    [Theory]
    [InlineData("admin", "lock")]
    [InlineData("staff", "role")]
    [InlineData("teacher", "delete")]
    [InlineData("student", "lock")]
    [InlineData("student", "role")]
    public async Task StaleToken_SessionRequestsRejectBeforeResourceLookup(string actor, string change)
    {
        await fixture.AsAsync("staff");
        var session = await CreateAsync(fixture.Body(fixture.Second));
        await fixture.AsAsync(actor);
        var token = Client.DefaultRequestHeaders.Authorization;
        await fixture.ChangeAccountAsync(actor, change);
        var expected = change == "delete" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        foreach (var path in new[] { Range(), $"{SessionsUrl}/{session.GetProperty("id").GetGuid()}", $"{SessionsUrl}/{Guid.NewGuid()}" })
            Assert.Equal(expected, (await Client.GetAsync(path)).StatusCode);
        Assert.Equal(expected, (await Client.PostAsJsonAsync(SessionsUrl, fixture.Body())).StatusCode);
        Assert.Equal(token, Client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task MembershipRevocation_RemovesSessionFromNextRangeAndDeniesDetailWithSameToken()
    {
        await fixture.AsAsync("staff");
        var session = await CreateAsync(fixture.Body(fixture.Second));
        await fixture.AsAsync("student");
        var token = Client.DefaultRequestHeaders.Authorization;
        Assert.Single((await ReadAsync(Range())).EnumerateArray());
        await fixture.RevokeAsync();
        Assert.Empty((await ReadAsync(Range())).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync(Range(classId: fixture.Second))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync($"{SessionsUrl}/{session.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(token, Client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SessionMigration_IsAcademicOnlyAndMatchesPostgresModel()
    {
        using var db = new AcademicDesignTimeFactory().CreateDbContext([]);
        var delta = db.GetService<IMigrator>().GenerateScript("20261112120100_AddTeacherProfileVersion");
        Assert.Contains("CREATE TABLE academic.sessions", delta);
        Assert.DoesNotContain("identity.", delta);
        Assert.DoesNotContain("enrollment.", delta);
        Assert.DoesNotContain("DROP TABLE", delta, StringComparison.OrdinalIgnoreCase);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private async Task<JsonElement> ReadAsync(string path)
    {
        using var response = await Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await JsonAsync(response);
        if (path.StartsWith(SessionsUrl, StringComparison.Ordinal))
        {
            if (result.ValueKind == JsonValueKind.Array)
                Assert.All(result.EnumerateArray(), AssertSessionShape);
            else AssertSessionShape(result);
        }
        return result;
    }

    private async Task<JsonElement> CreateAsync(Dictionary<string, object?> body)
    {
        using var response = await Client.PostAsJsonAsync(SessionsUrl, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await JsonAsync(response);
        AssertSessionShape(json);
        Assert.Equal(DateTimeOffset.Parse((string)body["startsAtUtc"]!, CultureInfo.InvariantCulture).ToUniversalTime(), json.GetProperty("startsAtUtc").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse((string)body["endsAtUtc"]!, CultureInfo.InvariantCulture).ToUniversalTime(), json.GetProperty("endsAtUtc").GetDateTimeOffset());
        Assert.EndsWith($"{SessionsUrl}/{json.GetProperty("id").GetGuid()}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        return json;
    }

    private static void AssertSessionShape(JsonElement value)
    {
        Assert.Equal(new[] { "classCode", "classId", "className", "createdAtUtc", "endsAtUtc", "examSlot", "id", "isCancelled", "lesson",
            "revision", "sessionType", "skills", "startsAtUtc", "teacherId", "teacherName", "unit" }, value.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(TimeSpan.Zero, value.GetProperty("startsAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(TimeSpan.Zero, value.GetProperty("endsAtUtc").GetDateTimeOffset().Offset);
    }

    private async Task<(string Name, string Email, long Version)> TeacherSnapshotAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var teacher = await scope.ServiceProvider.GetRequiredService<AcademicDbContext>().Teachers.AsNoTracking().SingleAsync(x => x.Id == fixture.Users["teacher"]);
        return (teacher.FullName, teacher.Email, teacher.ProfileVersion);
    }
}
