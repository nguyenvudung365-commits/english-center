using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Domain.Users;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class AcademicAttendanceBoundaryTests : IAsyncLifetime
{
    private readonly AttendanceApiFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public async Task DisposeAsync() => await fixture.DisposeAsync();

    [Fact]
    public async Task CompleteNamedLiveRosterTraversesThreePagesWithoutAcademicProjection()
    {
        var expected = await SeedLargeRosterAsync();
        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var roster = await fixture.ReadAsync($"{fixture.RosterUrl}?page={page}&pageSize=100");
            Assert.Equal(211, roster.GetProperty("total").GetInt32());
            Assert.Equal(page, roster.GetProperty("page").GetInt32());
            Assert.Equal(100, roster.GetProperty("pageSize").GetInt32());
            var rows = roster.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(page < 3 ? 100 : 11, rows.Length);
            foreach (var row in rows)
            {
                var id = row.GetProperty("studentId").GetGuid();
                Assert.True(expected.ContainsKey(id), $"Unexpected roster student {id}");
                Assert.Equal(expected[id].Name, row.GetProperty("fullName").GetString());
                Assert.Equal(expected[id].Code, row.GetProperty("studentCode").GetString());
                Assert.True(row.GetProperty("isActive").GetBoolean());
                Assert.Equal(JsonValueKind.Null, row.GetProperty("attendance").ValueKind);
                seen.Add(id);
            }
        }
        Assert.Equal(expected.Keys.OrderBy(x => x), seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        var pastEnd = await fixture.ReadAsync($"{fixture.RosterUrl}?page=4&pageSize=100");
        Assert.Equal(211, pastEnd.GetProperty("total").GetInt32());
        Assert.Empty(pastEnd.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task PageValidationRejectsInvalidValuesAndPastEndKeepsRealTotal()
    {
        var id = await RecordAsync();
        string[] paths = [fixture.RosterUrl,
            $"/api/academic/attendance?classId={fixture.Host.Second}",
            $"/api/academic/attendance/{id}/history"];
        foreach (var path in paths)
        {
            var separator = path.Contains('?') ? "&" : "?";
            foreach (var query in new[] { "page=0", "page=-1", "pageSize=0", "pageSize=101", "page=oops" })
                await ExpectAsync(path + separator + query, HttpStatusCode.BadRequest);
            var pastEnd = await fixture.ReadAsync(path + separator + "page=2147483647&pageSize=100");
            Assert.Equal(1, pastEnd.GetProperty("total").GetInt32());
            Assert.Empty(pastEnd.GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public async Task TeacherRequiresClassScopeAndStudentCannotReadPeersOrRoster()
    {
        var id = await RecordAsync();
        foreach (var suffix in new[] { "", "/summary" })
        {
            await ExpectAsync("/api/academic/attendance" + suffix, HttpStatusCode.BadRequest);
            await ExpectAsync($"/api/academic/attendance{suffix}?classId={fixture.Host.Other}", HttpStatusCode.Forbidden);
        }
        await fixture.AsAsync("student");
        foreach (var suffix in new[] { "", "/summary" })
            await ExpectAsync($"/api/academic/attendance{suffix}?studentId={fixture.Host.Users["other-student"]}", HttpStatusCode.Forbidden);
        await ExpectAsync(fixture.RosterUrl, HttpStatusCode.Forbidden);
        Assert.Equal(1, (await fixture.ReadAsync($"/api/academic/attendance/{id}/history")).GetProperty("total").GetInt32());
        await fixture.AsAsync("other-student");
        await ExpectAsync($"/api/academic/attendance/{id}/history", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MalformedIdsStatusesAndMismatchedClassSessionAreBadRequest()
    {
        await fixture.AsAsync("staff");
        foreach (var suffix in new[] { "", "/summary" })
        {
            var path = "/api/academic/attendance" + suffix + "?";
            foreach (var query in new[] { "classId=not-a-guid", $"classId={Guid.Empty}",
                "sessionId=not-a-guid", "studentId=not-a-guid", "status=1", "status=present", "status=Unknown",
                $"classId={fixture.Host.First}&sessionId={fixture.Session}" })
                await ExpectAsync(path + query, HttpStatusCode.BadRequest);
        }
        await ExpectAsync("/api/academic/sessions/not-a-guid/attendance", HttpStatusCode.BadRequest);
        await ExpectAsync($"/api/academic/sessions/{Guid.Empty}/attendance", HttpStatusCode.BadRequest);
        await ExpectAsync("/api/academic/attendance/not-a-guid/history", HttpStatusCode.BadRequest);
        await fixture.AsAsync("teacher");
        using var response = await fixture.Client.PutAsJsonAsync(
            $"/api/academic/sessions/{fixture.Session}/attendance/{Guid.Empty}", new { status = "Present", expectedRevision = (int?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("staff")]
    public async Task ManagerReadsAndReplaysWithDurableAuditButCannotWrite(string actor)
    {
        var id = await RecordAsync();
        await fixture.AsAsync(actor);
        var roster = await fixture.ReadAsync(fixture.RosterUrl);
        Assert.False(roster.GetProperty("canWrite").GetBoolean());
        Assert.Equal(1, (await fixture.ReadAsync("/api/academic/attendance")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await fixture.ReadAsync("/api/academic/attendance/summary")).GetProperty("recordedCount").GetInt32());
        using var denied = await fixture.PutAsync("Late", 1);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var before = await fixture.ReadAsync($"/api/academic/attendance/{id}/replays");
        Assert.Equal(0, before.GetProperty("total").GetInt32());
        using var replay = await fixture.Client.PostAsJsonAsync($"/api/academic/attendance/{id}/replay",
            new { revision = 1, reason = "  Đối soát Việt 🗓 '  " });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var dispatched = await AcademicScheduleFixture.JsonAsync(replay);
        Assert.Equal("Dispatched", dispatched.GetProperty("dispatchStatus").GetString());
        Assert.Equal(id, dispatched.GetProperty("attendanceId").GetGuid());
        Assert.Equal(1, dispatched.GetProperty("revision").GetInt32());
        var audit = await fixture.ReadAsync($"/api/academic/attendance/{id}/replays");
        Assert.Equal(1, audit.GetProperty("total").GetInt32());
        var attempt = Assert.Single(audit.GetProperty("items").EnumerateArray());
        Assert.Equal(dispatched.GetProperty("attemptId").GetGuid(), attempt.GetProperty("id").GetGuid());
        Assert.Equal(fixture.Host.Users[actor], attempt.GetProperty("requestedBy").GetGuid());
        Assert.Equal("Đối soát Việt 🗓 '", attempt.GetProperty("reason").GetString());
        Assert.Equal("Dispatched", attempt.GetProperty("dispatchStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, attempt.GetProperty("completedAtUtc").ValueKind);
        Assert.Equal(1, (await fixture.ReadAsync($"/api/academic/attendance/{id}/history")).GetProperty("total").GetInt32());
        var pastEnd = await fixture.ReadAsync($"/api/academic/attendance/{id}/replays?page=2&pageSize=1");
        Assert.Equal(1, pastEnd.GetProperty("total").GetInt32());
        Assert.Empty(pastEnd.GetProperty("items").EnumerateArray());
        foreach (var nonManager in new[] { "teacher", "student" })
        {
            await fixture.AsAsync(nonManager);
            await ExpectAsync($"/api/academic/attendance/{id}/replays", HttpStatusCode.Forbidden);
            using var forbidden = await fixture.Client.PostAsJsonAsync($"/api/academic/attendance/{id}/replay",
                new { revision = 1, reason = "not allowed" });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
    }

    [Fact]
    public async Task SummaryCountsSixActualStatusesNotUnrecordedAndDoesNotUsePageSize()
    {
        var sessions = new List<Guid> { fixture.Session };
        for (var hour = 12; hour <= 17; hour++)
            sessions.Add(await fixture.AddSessionAsync($"2027-01-01T{hour:D2}:00:00Z", $"2027-01-01T{hour:D2}:30:00Z"));
        fixture.Host.Clock.UtcNow = DateTimeOffset.Parse("2027-01-01T18:00:00Z");
        await fixture.AsAsync("teacher");
        string[] statuses = ["Present", "AbsentUnexcused", "AbsentExcused", "Late", "LateOver20Min", "LeftEarly"];
        for (var i = 0; i < statuses.Length; i++)
        {
            using var saved = await fixture.Client.PutAsJsonAsync(
                $"/api/academic/sessions/{sessions[i]}/attendance/{fixture.Student}",
                new { status = statuses[i], expectedRevision = (int?)null });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }
        var page = await fixture.ReadAsync($"/api/academic/attendance?classId={fixture.Host.Second}&pageSize=1");
        Assert.Equal(6, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var summary = await fixture.ReadAsync($"/api/academic/attendance/summary?classId={fixture.Host.Second}");
        Assert.Equal(6, summary.GetProperty("recordedCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("absentCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("lateCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("leftEarlyCount").GetInt32());
        foreach (var key in new[] { "present", "absentUnexcused", "absentExcused", "late", "lateOver20Min", "leftEarly" })
        {
            Assert.Equal(1, summary.GetProperty("counts").GetProperty(key).GetInt32());
            Assert.Equal(17, summary.GetProperty("percentages").GetProperty(key).GetInt32());
        }
        var unrecorded = await fixture.ReadAsync($"/api/academic/sessions/{sessions[^1]}/attendance");
        Assert.Equal(JsonValueKind.Null, Assert.Single(unrecorded.GetProperty("items").EnumerateArray()).GetProperty("attendance").ValueKind);
        var filtered = await fixture.ReadAsync($"/api/academic/attendance/summary?classId={fixture.Host.Second}&status=Present");
        Assert.Equal(1, filtered.GetProperty("recordedCount").GetInt32());
        Assert.Equal(100, filtered.GetProperty("percentages").GetProperty("present").GetInt32());
    }

    private async Task<Guid> RecordAsync()
    {
        using var response = await fixture.PutAsync("Present", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await AcademicScheduleFixture.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task ExpectAsync(string path, HttpStatusCode expected)
    {
        using var response = await fixture.Client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    private async Task<Dictionary<Guid, (string Name, string? Code)>> SeedLargeRosterAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var enrollment = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        var academic = scope.ServiceProvider.GetRequiredService<AcademicDbContext>();
        var existing = await identity.Users.SingleAsync(x => x.Id == fixture.Student);
        var expected = new Dictionary<Guid, (string Name, string? Code)> { [existing.Id] = (existing.FullName, existing.StudentCode) };
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= 213; i++)
        {
            var user = User.Create($"roster-{i}@example.test", $"Học viên Việt 🗓 {i:D3}", Roles.Student, existing.PasswordHash, now);
            user.AssignStudentCode($"HV{i + 1000:D6}-{now:yy}");
            if (i == 213) user.SetLocked(true);
            identity.Users.Add(user);
            enrollment.Students.Add(new Student(user.Id, user.FullName, user.Email));
            var registration = new Registration(user.Id, fixture.Host.Second, 0, now.UtcDateTime, 3, 5, 2);
            enrollment.Enrollments.Add(registration);
            if (i == 211) enrollment.Entry(registration).Property(x => x.IsActive).CurrentValue = false;
            if (i == 212) enrollment.Entry(registration).Property(x => x.Status).CurrentValue = "Cancelled";
            if (i <= 210) expected.Add(user.Id, (user.FullName, user.StudentCode));
        }
        var currentClass = await academic.Classes.SingleAsync(x => x.Id == fixture.Host.Second);
        academic.Entry(currentClass).Property(x => x.MaxStudents).CurrentValue = 250;
        var snapshot = await enrollment.Classes.SingleAsync(x => x.ClassId == fixture.Host.Second);
        enrollment.Entry(snapshot).Property(x => x.MaxStudents).CurrentValue = 250;
        await identity.SaveChangesAsync();
        await enrollment.SaveChangesAsync();
        await academic.SaveChangesAsync();
        Assert.Empty(await academic.ClassStudents.Where(x => x.ClassId == fixture.Host.Second).ToListAsync());
        return expected;
    }
}
