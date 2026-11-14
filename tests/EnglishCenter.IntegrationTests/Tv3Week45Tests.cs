using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.PlacementTests;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Application.Invoices;
using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishCenter.IntegrationTests;

// Cùng fixture API thật của tuần 3; không giả lập API điểm danh/nộp/chấm chưa được TV2 bàn giao.
public sealed partial class Tv3Week3Tests
{
    private const string PlacementUrl = "/api/enrollment/placement-tests";
    private const string WarningsUrl = "/api/enrollment/warnings";
    private async Task<EnrollmentResponse> EnrollValue() =>
        (await (await Enroll()).Content.ReadFromJsonAsync<EnrollmentResponse>())!;
    private string LimitsUrl(Guid id) => $"{Url}/{id}/limits";
    private async Task SetLimits(Guid id, int absence = 0, int late = 0, int skipped = 0) =>
        (await _client.PutAsJsonAsync(LimitsUrl(id), new LimitsRequest(absence, late, skipped))).EnsureSuccessStatusCode();
    private Task<LimitsResponse?> Limits(Guid id) => _client.GetFromJsonAsync<LimitsResponse>(LimitsUrl(id));
    private async Task Attendance(Guid session, string status, DateTime at, Guid? student = null, Guid? classId = null)
    {
        using var scope = _factory.Services.CreateScope();
        await ActivatorUtilities.CreateInstance<AttendanceRecordedHandler>(scope.ServiceProvider)
            .HandleAsync(new(session, classId ?? _class, student ?? _student, status, at));
    }
    private async Task Missed(Guid assignment)
    {
        using var scope = _factory.Services.CreateScope();
        await ActivatorUtilities.CreateInstance<AssignmentMissedHandler>(scope.ServiceProvider)
            .HandleAsync(new(assignment, _class, _student));
    }
    private PlacementTestRequest Placement() => new(_student, "Phòng 101", "Offline", DateTimeOffset.UtcNow.AddDays(2), "IELTS đầu vào", _teacher);

    [Theory]
    [InlineData("Present", 0, 0)]
    [InlineData("AbsentUnexcused", 1, 0)]
    [InlineData("AbsentExcused", 0, 0)]
    [InlineData("Late", 0, 1)]
    [InlineData("LateOver20Min", 0, 1)]
    [InlineData("LeftEarly", 0, 0)]
    public async Task Week4_AttendanceSixStatuses_MapToExpectedViolation(string status, int absence, int late)
    {
        var enrollment = await EnrollValue();
        await SetLimits(enrollment.Id);
        var session = Guid.NewGuid(); var at = DateTime.UtcNow;
        await Attendance(session, status, at);
        await Attendance(session, status, at); // EventId mới, cùng trạng thái/giờ.
        var limits = (await Limits(enrollment.Id))!;
        Assert.Equal(absence, limits.AbsenceCount);
        Assert.Equal(late, limits.LateCount);
        Assert.Equal(absence + late, _factory.WarningEvents.Count);
        using var scope = _factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().AttendanceSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Week4_AttendanceCorrection_Recalculates_IgnoresOldEvents_AndRetainsWarningHistory()
    {
        var enrollment = await EnrollValue();
        await SetLimits(enrollment.Id);
        var session = Guid.NewGuid(); var at = DateTime.UtcNow.AddHours(-3);
        await Attendance(session, "AbsentUnexcused", at);
        await Attendance(session, "Late", at.AddMinutes(1));
        await Attendance(session, "Present", at.AddMinutes(2));
        await Attendance(session, "AbsentUnexcused", at); // Cũ hơn: không ghi đè.
        Assert.Equal(0, (await Limits(enrollment.Id))!.AbsenceCount);
        Assert.Equal(0, (await Limits(enrollment.Id))!.LateCount);
        Assert.Equal(2, _factory.WarningEvents.Count);
        await Attendance(session, "AbsentUnexcused", at.AddMinutes(3));
        Assert.Equal(1, (await Limits(enrollment.Id))!.AbsenceCount);
        Assert.Equal(2, _factory.WarningEvents.Count); // Cùng loại không tạo cảnh báo lần hai.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Attendance(session, "Present", at.AddMinutes(3)));
        Assert.Equal(1, (await Limits(enrollment.Id))!.AbsenceCount);
    }

    [Fact]
    public async Task Week5_Limits_OnlyWarnAboveThreshold_ChangesReevaluate_AndMissedAssignmentsDeduplicate()
    {
        var enrollment = await EnrollValue();
        await SetLimits(enrollment.Id, absence: 1, late: 1, skipped: 1);
        var at = DateTime.UtcNow;
        await Attendance(Guid.NewGuid(), "AbsentUnexcused", at);
        await Attendance(Guid.NewGuid(), "Late", at);
        var assignment = Guid.NewGuid();
        await Missed(assignment); await Missed(assignment);
        Assert.Empty(_factory.WarningEvents);
        Assert.Equal(1, (await Limits(enrollment.Id))!.SkippedAssignmentCount);
        await SetLimits(enrollment.Id);
        Assert.Equal(new[] { "Absence", "Late", "SkippedAssignment" }, _factory.WarningEvents.Select(x => x.WarningType).Order().ToArray());
        await Missed(Guid.NewGuid());
        Assert.Equal(2, (await Limits(enrollment.Id))!.SkippedAssignmentCount);
        Assert.Equal(3, _factory.WarningEvents.Count);
        var warning = _factory.WarningEvents.First();
        Assert.Equal(_student, warning.StudentId); Assert.Equal(_class, warning.ClassId); Assert.Equal(enrollment.Id, warning.EnrollmentId);
        Assert.Equal(warning.WarningId, warning.EventId);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{WarningsUrl}/{warning.WarningId}/replay", null)).StatusCode);
        Assert.Equal(2, _factory.WarningEvents.Count(x => x.EventId == warning.EventId));
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Warnings.CountAsync());
    }

    [Fact]
    public async Task Week4_InvalidAttendanceAndUnknownEnrollment_DoNotMutateOrPublish()
    {
        var enrollment = await EnrollValue();
        await Assert.ThrowsAsync<ArgumentException>(() => Attendance(Guid.NewGuid(), "Invalid", DateTime.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => Attendance(Guid.Empty, "Present", DateTime.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => Attendance(Guid.NewGuid(), "Present", DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local)));
        await Assert.ThrowsAsync<EnrollmentFailure>(() => Attendance(Guid.NewGuid(), "Late", DateTime.UtcNow, classId: Guid.NewGuid()));
        Assert.Equal(0, (await Limits(enrollment.Id))!.LateCount);
        Assert.Empty(_factory.WarningEvents);
    }

    [Fact]
    public async Task Week5_MonitoringSaveFailure_RollsBackSnapshotCountsAndWarnings()
    {
        var enrollment = await EnrollValue(); await SetLimits(enrollment.Id);
        _factory.EnrollmentSave.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Attendance(Guid.NewGuid(), "AbsentUnexcused", DateTime.UtcNow));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        Assert.Empty(await db.AttendanceSnapshots.ToListAsync()); Assert.Empty(await db.Warnings.ToListAsync());
        Assert.Equal(0, (await Limits(enrollment.Id))!.AbsenceCount); Assert.Empty(_factory.WarningEvents);
    }

    [Theory]
    [InlineData("Staff", true)]
    [InlineData("Admin", true)]
    [InlineData("Student", false)]
    [InlineData("Teacher", false)]
    public async Task Week5_WriteApis_RequireManager(string name, bool allowed)
    {
        var enrollment = await EnrollValue(); var invoice = await GetInvoice(enrollment.Id);
        var placement = (await (await _client.PostAsJsonAsync(PlacementUrl, Placement())).Content.ReadFromJsonAsync<PlacementTestResponse>())!;
        await SetLimits(enrollment.Id); await Missed(Guid.NewGuid());
        var warning = Assert.Single(_factory.WarningEvents);
        async Task<HttpResponseMessage[]> WriteAll() => await Task.WhenAll(
            _client.PutAsJsonAsync(LimitsUrl(enrollment.Id), new LimitsRequest(2, 3, 4)),
            _client.PostAsJsonAsync(PlacementUrl, Placement()),
            _client.PutAsJsonAsync($"{PlacementUrl}/{placement.Id}", new UpdatePlacementTestRequest("Phòng 102", "Online", DateTimeOffset.UtcNow.AddDays(3), "Test", "Cancelled", null)),
            _client.PostAsync($"/api/finance/invoices/{invoice.Id}/pay", null),
            _client.PostAsync($"{WarningsUrl}/{warning.WarningId}/replay", null));
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.All(await WriteAll(), x => Assert.Equal(HttpStatusCode.Unauthorized, x.StatusCode));
        await Login(name);
        var result = await WriteAll();
        if (allowed) Assert.All(result, x => Assert.True(x.IsSuccessStatusCode, x.StatusCode.ToString()));
        else Assert.All(result, x => Assert.Equal(HttpStatusCode.Forbidden, x.StatusCode));
    }

    [Fact]
    public async Task Week5_StudentReadsOnlyOwnWarningsPlacementTestsAndInvoices_EvenWithForgedFilters()
    {
        var enrollment = await EnrollValue(); var invoice = await GetInvoice(enrollment.Id);
        await SetLimits(enrollment.Id); await Missed(Guid.NewGuid());
        var placement = (await (await _client.PostAsJsonAsync(PlacementUrl, Placement())).Content.ReadFromJsonAsync<PlacementTestResponse>())!;
        await Login("Other");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(LimitsUrl(enrollment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{PlacementUrl}/{placement.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/finance/invoices/{invoice.Id}")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<PageResult<WarningResponse>>($"{WarningsUrl}?studentId={_student}"))!.Items);
        Assert.Empty((await _client.GetFromJsonAsync<PageResult<PlacementTestResponse>>($"{PlacementUrl}?studentId={_student}"))!.Items);
        Assert.Empty((await _client.GetFromJsonAsync<InvoicePage>($"/api/finance/invoices?studentId={_student}"))!.Items);
        await Login("Student");
        Assert.Single((await _client.GetFromJsonAsync<PageResult<WarningResponse>>(WarningsUrl))!.Items);
        Assert.Single((await _client.GetFromJsonAsync<PageResult<PlacementTestResponse>>(PlacementUrl))!.Items);
        Assert.Single((await _client.GetFromJsonAsync<InvoicePage>("/api/finance/invoices"))!.Items);
        await Login("Teacher");
        foreach (var url in new[] { WarningsUrl, PlacementUrl, "/api/finance/invoices", LimitsUrl(enrollment.Id) })
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Week5_Placement_CreateReadUpdate_PreservesCreatorAndStudent_AndConvertsTimezone()
    {
        var at = DateTimeOffset.UtcNow.AddDays(5).ToOffset(TimeSpan.FromHours(7));
        var response = await _client.PostAsJsonAsync(PlacementUrl, Placement() with { ScheduledAt = at });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<PlacementTestResponse>())!;
        Assert.Equal(_student, value.StudentId); Assert.Equal(_teacher, value.TeacherId);
        Assert.Equal(at.UtcDateTime.Ticks / 10 * 10, value.ScheduledAtUtc.Ticks);
        Assert.Equal(DateTimeKind.Utc, value.ScheduledAtUtc.Kind);
        Assert.Equal(value, await _client.GetFromJsonAsync<PlacementTestResponse>(response.Headers.Location));
        var updated = await _client.PutAsJsonAsync($"{PlacementUrl}/{value.Id}", new UpdatePlacementTestRequest("Zoom", "Online", at, "Test mới", "Done", null));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var changed = (await updated.Content.ReadFromJsonAsync<PlacementTestResponse>())!;
        Assert.Equal(value.CreatedBy, changed.CreatedBy); Assert.Equal(value.CreatedAtUtc, changed.CreatedAtUtc);
        Assert.Equal(_student, changed.StudentId); Assert.Equal("Done", changed.Status); Assert.Null(changed.TeacherId);
    }

    [Fact]
    public async Task Week5_InvalidLimitsAndPlacement_Return400_UnknownResourcesReturn404()
    {
        var enrollment = await EnrollValue();
        foreach (var invalid in new[] { new LimitsRequest(-1, 0, 0), new LimitsRequest(0, null, 0), new LimitsRequest(0, 0, -1) })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync(LimitsUrl(enrollment.Id), invalid)).StatusCode);
        foreach (var invalid in new[] { Placement() with { StudentId = Guid.NewGuid() }, Placement() with { TeacherId = _student },
            Placement() with { Mode = "Invalid" }, Placement() with { Location = " " }, Placement() with { ScheduledAt = null },
            Placement() with { ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1) }, Placement() with { TestName = new string('x', 201) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync(PlacementUrl, invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{PlacementUrl}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync(LimitsUrl(Guid.NewGuid()), new LimitsRequest(0, 0, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"/api/finance/invoices/{Guid.NewGuid()}/pay", null)).StatusCode);
    }

    [Fact]
    public async Task Week5_Pagination_ValidatesAndCapsPageSize()
    {
        await EnrollValue(); await _client.PostAsJsonAsync(PlacementUrl, Placement());
        foreach (var url in new[] { WarningsUrl, PlacementUrl, "/api/finance/invoices" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url + "?page=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url + "?pageSize=0")).StatusCode);
            var json = await _client.GetFromJsonAsync<JsonElement>(url + "?pageSize=999");
            Assert.Equal(100, json.GetProperty("pageSize").GetInt32());
            var empty = await _client.GetFromJsonAsync<JsonElement>(url + "?page=1000");
            Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task Week5_Payment_IsAtomicAuditedAndReplayable_EnrollmentReplayPreservesPaidState()
    {
        var enrollment = await EnrollValue(); var invoice = await GetInvoice(enrollment.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/finance/invoices/{invoice.Id}/payment/replay", null)).StatusCode);
        var response = await _client.PostAsJsonAsync($"/api/finance/invoices/{invoice.Id}/pay", new { ConfirmedBy = _student, Amount = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paid = (await response.Content.ReadFromJsonAsync<InvoiceResponse>())!;
        Assert.Equal("Paid", paid.Status); Assert.NotNull(paid.PaidAtUtc); Assert.NotEqual(_student, paid.ConfirmedBy);
        Assert.Equal(invoice.Amount, paid.Amount);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/finance/invoices/{invoice.Id}/pay", null)).StatusCode);
        var e = Assert.Single(_factory.PaymentEvents);
        Assert.Equal(invoice.Id, e.InvoiceId); Assert.Equal(_student, e.StudentId); Assert.Equal(enrollment.Id, e.EnrollmentId);
        Assert.Equal(paid.PaidAtUtc, e.PaidAtUtc); Assert.Equal(invoice.Id, e.EventId);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"/api/finance/invoices/{invoice.Id}/payment/replay", null)).StatusCode);
        Assert.Equal(2, _factory.PaymentEvents.Count);
        Assert.All(_factory.PaymentEvents, x => Assert.Equal(e, x));
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Url}/{enrollment.Id}/replay", null)).StatusCode);
        Assert.Equal(paid, await GetInvoice(enrollment.Id));
        await Login("Student");
        Assert.Equal(paid, await _client.GetFromJsonAsync<InvoiceResponse>($"/api/finance/invoices/{invoice.Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsync($"/api/finance/invoices/{invoice.Id}/payment/replay", null)).StatusCode);
    }

    [PgFact]
    public async Task Week5_PostgreSql_ConcurrentAttendanceAndMissedAssignments_DoNotLoseCountsOrDuplicateWarnings()
    {
        var enrollment = await EnrollValue(); await SetLimits(enrollment.Id);
        var at = DateTime.UtcNow; var assignment = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Attendance(Guid.NewGuid(), "Late", at)));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Missed(assignment)));
        Assert.Equal(12, (await Limits(enrollment.Id))!.LateCount);
        Assert.Equal(1, (await Limits(enrollment.Id))!.SkippedAssignmentCount);
        Assert.Equal(2, _factory.WarningEvents.Count);
        var session = Guid.NewGuid();
        await Task.WhenAll(Attendance(session, "AbsentUnexcused", at), Attendance(session, "Present", at.AddMinutes(1)));
        Assert.Equal(0, (await Limits(enrollment.Id))!.AbsenceCount);
    }

    [PgFact]
    public async Task Week5_PostgreSql_ConcurrentPayments_OneSucceedsAndOneEventIsPublished()
    {
        var enrollment = await EnrollValue(); var invoice = await GetInvoice(enrollment.Id);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _client.PostAsync($"/api/finance/invoices/{invoice.Id}/pay", null)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(_factory.PaymentEvents);
        Assert.Equal("Paid", (await GetInvoice(enrollment.Id)).Status);
    }

    [PgFact]
    public async Task Week5_PostgreSql_MigrationUpgrade_PreservesWeek3EnrollmentsAndSeats()
    {
        var enrollment = await EnrollValue();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
        // Database tạm của fixture: đưa về schema tuần 3 rồi nâng cấp lại, kiểm tra dữ liệu còn nguyên.
        await db.GetService<IMigrator>().MigrateAsync("20261105000727_InitialEnrollment");
        Assert.Equal(enrollment.Id, (await db.Enrollments.AsNoTracking().SingleAsync()).Id);
        await db.Database.MigrateAsync();
        Assert.Equal(1, (await db.Classes.AsNoTracking().SingleAsync()).CurrentStudents);
        Assert.Equal(enrollment.Id, (await db.Enrollments.AsNoTracking().SingleAsync()).Id);
        await SetLimits(enrollment.Id); await Missed(Guid.NewGuid());
        Assert.Single(_factory.WarningEvents);
    }
}
