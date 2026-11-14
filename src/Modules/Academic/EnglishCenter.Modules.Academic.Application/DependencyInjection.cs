using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Academic.Application.Practice;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

namespace EnglishCenter.Modules.Academic.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicApplication(this IServiceCollection services)
    {
        services.AddScoped<ClassService>();
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.IClassDirectory, ClassDirectory>();
        services.AddScoped<CourseworkAccess>();
        services.AddScoped<CourseworkEvents>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<GradingService>();
        services.AddScoped<CourseworkReconciler>();
        services.AddScoped<PracticeService>();
        services.AddScoped<CourseworkDirectory>();
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.IAssignmentDirectory>(sp => sp.GetRequiredService<CourseworkDirectory>());
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.ICourseworkMetrics>(sp => sp.GetRequiredService<CourseworkDirectory>());
        // TV1 thay bằng bản triển khai Session thật (IsAvailable = true) khi US-08 được merge.
        services.TryAddScoped<ISessionCatalog, UnavailableSessionCatalog>();
        services.AddScoped<StudentClasses>();
        services.AddIntegrationEventHandler<StudentEnrolledIntegrationEvent, StudentEnrolledHandler>();
        services.AddIntegrationEventHandler<UserRegisteredIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        services.AddIntegrationEventHandler<UserProfileUpdatedIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        return services;
    }
}
