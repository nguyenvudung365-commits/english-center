using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Application.Assignments;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Academic.Application.Practice;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;

namespace EnglishCenter.Modules.Academic.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicApplication(this IServiceCollection services)
    {
        services.AddScoped<ClassService>();
        services.AddScoped<ClassAccess>();
        services.AddScoped<Sessions.SessionService>();
        services.AddScoped<Sessions.SessionBatchService>();
        services.AddScoped<Attendance.AttendanceService>();
        services.AddScoped<Statistics.StatisticsService>();
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.ISessionDirectory, Sessions.SessionDirectory>();
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.IClassDirectory, ClassDirectory>();
        services.AddScoped<CourseworkAccess>();
        services.AddScoped<CourseworkEvents>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<GradingService>();
        services.AddScoped<MaterialService>();
        services.AddScoped<CourseworkReconciler>();
        services.AddScoped<PracticeService>();
        services.AddScoped<CourseworkDirectory>();
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.IAssignmentDirectory>(sp => sp.GetRequiredService<CourseworkDirectory>());
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.ICourseworkMetrics>(sp => sp.GetRequiredService<CourseworkDirectory>());
        // Bài tập gắn với buổi học thật của TV1 (US-08): bắt buộc gán buổi cùng lớp trước khi công bố (AC-05).
        services.AddScoped<ISessionCatalog, Sessions.SessionCatalog>();
        services.AddScoped<StudentClasses>();
        services.AddIntegrationEventHandler<StudentEnrolledIntegrationEvent, StudentEnrolledHandler>();
        services.AddIntegrationEventHandler<UserRegisteredIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        services.AddIntegrationEventHandler<UserProfileUpdatedIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        return services;
    }
}
