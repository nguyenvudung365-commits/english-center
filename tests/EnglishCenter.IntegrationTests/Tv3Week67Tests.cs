using System.Net;
using System.Net.Http.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Application.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed partial class Tv3Week3Tests
{
    private const string StatisticsUrl = "/api/enrollment/statistics/mine";
    private const string InvoicesUrl = "/api/finance/invoices";

    [Fact]
    public async Task Week6_CompletedClasses_UseVietnamDateAndExcludeCancelledAndOtherStudents()
    {
        await Login("Other");
        var otherToken = _client.DefaultRequestHeaders.Authorization;
        await Login("Student");
        var today = new DateOnly(2026, 11, 14);
        var midnight = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
        _factory.Clock.UtcNow = midnight.AddSeconds(-1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            Registration AddEnrollment(Guid studentId, DateOnly endDate)
            {
                var classId = Guid.NewGuid();
                db.Classes.Add(new(classId, classId.ToString("N"), endDate.AddDays(-30), endDate, 20, 0));
                var registration = new Registration(studentId, classId, 0, midnight.UtcDateTime.AddDays(-30), 0, 0, 0);
                db.Enrollments.Add(registration);
                return registration;
            }

            AddEnrollment(_student, today.AddDays(-2));
            AddEnrollment(_student, today.AddDays(-1));
            AddEnrollment(_student, today);
            AddEnrollment(_student, today.AddDays(1));
            AddEnrollment(_other, today.AddDays(-2));
            var cancelled = AddEnrollment(_student, today.AddDays(-2));
            await db.SaveChangesAsync();
            await db.Enrollments.Where(x => x.Id == cancelled.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "Cancelled"));
        }

        Assert.Equal(new EnrollmentStatisticsResponse(1),
            await _client.GetFromJsonAsync<EnrollmentStatisticsResponse>(StatisticsUrl));
        _factory.Clock.UtcNow = midnight;
        Assert.Equal(new EnrollmentStatisticsResponse(2),
            await _client.GetFromJsonAsync<EnrollmentStatisticsResponse>($"{StatisticsUrl}?studentId={_other}"));
        _factory.Clock.UtcNow = midnight.AddDays(1);
        Assert.Equal(new EnrollmentStatisticsResponse(3),
            await _client.GetFromJsonAsync<EnrollmentStatisticsResponse>(StatisticsUrl));
        _client.DefaultRequestHeaders.Authorization = otherToken;
        Assert.Equal(new EnrollmentStatisticsResponse(1),
            await _client.GetFromJsonAsync<EnrollmentStatisticsResponse>($"{StatisticsUrl}?studentId={_student}"));
    }

    [Fact]
    public async Task Week6_Statistics_RequiresStudentAndReturnsZeroForEmptyHistory()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(StatisticsUrl)).StatusCode);
        foreach (var role in new[] { "Teacher", "Staff", "Admin" })
        {
            await Login(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(StatisticsUrl)).StatusCode);
        }

        await Login("Student");
        Assert.Equal(new EnrollmentStatisticsResponse(0),
            await _client.GetFromJsonAsync<EnrollmentStatisticsResponse>(StatisticsUrl));
    }

    [Fact]
    public async Task Week7_EnrollmentInvoiceAndWarning_FlowKeepsEachStudentsDataSeparate()
    {
        var enrollment = await EnrollValue();
        var otherEnrollmentResponse = await Enroll(_other);
        Assert.Equal(HttpStatusCode.Created, otherEnrollmentResponse.StatusCode);
        var otherEnrollment = (await otherEnrollmentResponse.Content.ReadFromJsonAsync<EnrollmentResponse>())!;
        var invoice = await GetInvoice(enrollment.Id);
        Assert.Equal("Unpaid", invoice.Status);
        Assert.Equal(enrollment.TuitionFee, invoice.Amount);
        Assert.Equal(DateOnly.FromDateTime(enrollment.EnrolledAtUtc.AddHours(7)).AddDays(7), invoice.DueDate);
        Assert.Equal(_other, (await GetInvoice(otherEnrollment.Id)).StudentId);

        await SetLimits(enrollment.Id, absence: 1);
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var timestamp = DateTime.UtcNow;
        await bus.PublishAsync(new AttendanceRecordedIntegrationEvent(Guid.NewGuid(), _class, _student, "AbsentUnexcused", timestamp));
        Assert.Empty(_factory.WarningEvents);
        var secondSession = Guid.NewGuid();
        await bus.PublishAsync(new AttendanceRecordedIntegrationEvent(secondSession, _class, _student, "AbsentUnexcused", timestamp));
        await bus.PublishAsync(new AttendanceRecordedIntegrationEvent(secondSession, _class, _student, "AbsentUnexcused", timestamp));
        var warning = Assert.Single(_factory.WarningEvents);
        Assert.Equal(enrollment.Id, warning.EnrollmentId);
        Assert.Equal(_student, warning.StudentId);
        Assert.Equal(_class, warning.ClassId);
        Assert.Equal("Absence", warning.WarningType);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"{InvoicesUrl}/{invoice.Id}/pay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"{InvoicesUrl}/{invoice.Id}/pay", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{Url}/{enrollment.Id}/replay", null)).StatusCode);
        Assert.Equal(2, _factory.Invoices.Count);
        Assert.Equal(invoice.Id, Assert.Single(_factory.PaymentEvents).InvoiceId);

        await Login("Student");
        var ownInvoices = await _client.GetFromJsonAsync<InvoicePage>($"{InvoicesUrl}?studentId={_other}");
        var paid = Assert.Single(ownInvoices!.Items);
        Assert.Equal(invoice.Id, paid.Id);
        Assert.Equal("Paid", paid.Status);
        Assert.Equal(invoice.Amount, paid.Amount);
        var ownWarnings = await _client.GetFromJsonAsync<PageResult<WarningResponse>>($"{WarningsUrl}?studentId={_other}");
        Assert.Equal(warning.WarningId, Assert.Single(ownWarnings!.Items).Id);

        await Login("Other");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{enrollment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{InvoicesUrl}/{invoice.Id}")).StatusCode);
        var otherInvoices = await _client.GetFromJsonAsync<InvoicePage>($"{InvoicesUrl}?studentId={_student}");
        var unpaid = Assert.Single(otherInvoices!.Items);
        Assert.Equal(otherEnrollment.Id, unpaid.EnrollmentId);
        Assert.Equal("Unpaid", unpaid.Status);
        var otherWarnings = await _client.GetFromJsonAsync<PageResult<WarningResponse>>($"{WarningsUrl}?studentId={_student}");
        Assert.Empty(otherWarnings!.Items);
    }
}
