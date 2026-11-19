using System.Net;
using System.Net.Http.Json;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Application.Monitoring;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using EnglishCenter.Modules.MediaNotification.Application.Notifications;
using EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed partial class Tv3Week3Tests
{
    private const string MyEnrollmentsUrl = Url + "/mine";
    private const string NotificationsUrl = "/api/media-notification/notifications";

    [Fact]
    public async Task MyEnrollments_ListsOnlyCurrentStudent_WithClassDetailsHistoryAndStablePagination()
    {
        var first = await EnrollValue();
        _class = await CreateClass();
        var second = await EnrollValue();
        Assert.Equal(HttpStatusCode.Created, (await Enroll(_other)).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>();
            await db.Enrollments.Where(x => x.Id == first.Id).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Status, "Cancelled"));
            await db.Enrollments.Where(x => x.Id == second.Id).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.IsActive, false).SetProperty(x => x.EnrolledAt, first.EnrolledAtUtc));
        }

        await Login("Student");
        var all = (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>($"{MyEnrollmentsUrl}?studentId={_other}"))!;
        Assert.Equal(2, all.Total);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), all.Items.Select(x => x.Id));
        var cancelled = Assert.Single(all.Items, x => x.Id == first.Id);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.False(cancelled.IsActive);
        var revoked = Assert.Single(all.Items, x => x.Id == second.Id);
        Assert.Equal("Active", revoked.Status);
        Assert.False(revoked.IsActive);
        Assert.Equal(second.ClassId, revoked.ClassId);
        Assert.Equal(second.TuitionFee, revoked.TuitionFee);
        Assert.StartsWith("TV3-", revoked.ClassCode);
        Assert.Equal(Today.AddDays(-30), revoked.StartDate);
        Assert.Equal(Today.AddDays(30), revoked.EndDate);
        Assert.Equal(first.EnrolledAtUtc, revoked.EnrolledAtUtc);

        var page1 = (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>($"{MyEnrollmentsUrl}?page=1&pageSize=1"))!;
        var page2 = (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>($"{MyEnrollmentsUrl}?page=2&pageSize=1"))!;
        Assert.Equal(2, page1.Total);
        Assert.Equal(all.Items[0], Assert.Single(page1.Items));
        Assert.Equal(all.Items[1], Assert.Single(page2.Items));
        var empty = (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>($"{MyEnrollmentsUrl}?page=3&pageSize=1"))!;
        Assert.Equal(2, empty.Total);
        Assert.Empty(empty.Items);

        await Login("Other");
        var other = (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>($"{MyEnrollmentsUrl}?studentId={_student}"))!;
        Assert.Equal(1, other.Total);
        Assert.DoesNotContain(Assert.Single(other.Items).Id, new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task MyEnrollments_RequiresActiveStudent_ValidatesPaginationAndReturnsEmptyHistory()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(MyEnrollmentsUrl)).StatusCode);
        foreach (var role in new[] { "Teacher", "Staff", "Admin" })
        {
            await Login(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(MyEnrollmentsUrl)).StatusCode);
        }
        await Login("Student");
        var empty = (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>(MyEnrollmentsUrl))!;
        Assert.Equal(0, empty.Total);
        Assert.Empty(empty.Items);
        foreach (var query in new[] { "page=0", "page=1000001", "pageSize=0", "page=abc" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"{MyEnrollmentsUrl}?{query}")).StatusCode);
        Assert.Equal(100, (await _client.GetFromJsonAsync<PageResult<MyEnrollmentResponse>>($"{MyEnrollmentsUrl}?pageSize=101"))!.PageSize);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.SingleAsync(x => x.Id == _student)).SetLocked(true);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(MyEnrollmentsUrl)).StatusCode);
    }

    [Fact]
    public async Task WarningNotification_RealEnrollmentWarningNotifiesOwnerOnly_AndReplayPreservesReadState()
    {
        var enrollment = await EnrollValue();
        await SetLimits(enrollment.Id);
        await Attendance(Guid.NewGuid(), "AbsentUnexcused", DateTime.UtcNow);
        var warning = Assert.Single(_factory.WarningEvents);
        Assert.Equal(_student, warning.StudentId);
        Assert.Equal(enrollment.Id, warning.EnrollmentId);

        await Login("Student");
        var notifications = (await _client.GetFromJsonAsync<NotificationListResponse>(NotificationsUrl))!;
        var notification = Assert.Single(notifications.Items, x => x.Kind == "WarningIssued");
        Assert.Equal("Warning", notification.TargetType);
        Assert.Equal(warning.WarningId, notification.TargetId);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"{NotificationsUrl}/{notification.Id}/read", null)).StatusCode);
        var read = Assert.Single((await _client.GetFromJsonAsync<NotificationListResponse>(NotificationsUrl))!.Items, x => x.Kind == "WarningIssued");
        Assert.True(read.IsRead);
        Assert.NotNull(read.ReadAtUtc);

        await Login("Staff");
        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"{WarningsUrl}/{warning.WarningId}/replay", null)).StatusCode);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IEventBus>()
                .PublishAsync(warning with { EventId = Guid.NewGuid() }, default, throwOnHandlerFailure: true);
        await Login("Student");
        var afterReplay = (await _client.GetFromJsonAsync<NotificationListResponse>(NotificationsUrl))!;
        Assert.Equal(read, Assert.Single(afterReplay.Items, x => x.Kind == "WarningIssued"));
        Assert.Equal(notifications.UnreadCount - 1, afterReplay.UnreadCount);

        await Login("Other");
        var other = (await _client.GetFromJsonAsync<NotificationListResponse>($"{NotificationsUrl}?studentId={_student}"))!;
        Assert.Empty(other.Items);
        Assert.Equal(0, other.UnreadCount);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync($"{NotificationsUrl}/{notification.Id}/read", null)).StatusCode);
    }

    [PgFact]
    public async Task WarningNotification_ConcurrentPostgresReplayCreatesOneNotification()
    {
        var enrollment = await EnrollValue();
        await SetLimits(enrollment.Id);
        await Missed(Guid.NewGuid());
        var warning = Assert.Single(_factory.WarningEvents);
        using (var missingNotification = _factory.Services.CreateScope())
            await missingNotification.ServiceProvider.GetRequiredService<MediaNotificationDbContext>().Notifications
                .Where(x => x.Kind == "WarningIssued").ExecuteDeleteAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _client.PostAsync($"{WarningsUrl}/{warning.WarningId}/replay", null)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<MediaNotificationDbContext>().Notifications
            .Where(x => x.Kind == "WarningIssued").ToListAsync();
        var notification = Assert.Single(stored);
        Assert.Equal(_student, notification.RecipientId);
        Assert.Equal(warning.WarningId, notification.SourceId);
        Assert.Equal(warning.WarningId, notification.TargetId);
    }
}
