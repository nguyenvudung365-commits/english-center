using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.MediaNotification.Application.Faqs;
using EnglishCenter.Modules.MediaNotification.Application.Feed;
using EnglishCenter.Modules.MediaNotification.Application.Feedback;
using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.MediaNotification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaNotificationApplication(this IServiceCollection services)
    {
        services.AddScoped<EnglishCenter.Modules.MediaNotification.Contracts.ISubmissionFileAccess, Files.SubmissionFileAccess>();
        services.AddScoped<EnglishCenter.Modules.MediaNotification.Contracts.IAvatarFileAccess, Files.AvatarFileAccess>();
        services.AddScoped<EnglishCenter.Modules.MediaNotification.Contracts.IClassFileAccess, Files.ClassFileAccess>();
        services.AddScoped<Files.FileService>();
        services.AddScoped<MediaAccess>();
        services.AddScoped<NotificationService>();
        services.AddScoped<FeedbackService>();
        services.AddScoped<FaqService>();
        services.AddScoped<ClassFeedService>();
        services.AddIntegrationEventHandler<StudentEnrolledIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<InvoicePaidIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<WarningIssuedIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<AssignmentPublishedIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<SubmissionGradedIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<AttendanceRevisionRecordedIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<SessionScheduleChangedIntegrationEvent, NotificationHandlers>();
        services.AddIntegrationEventHandler<AssignmentMissedIntegrationEvent, NotificationHandlers>();
        return services;
    }
}
