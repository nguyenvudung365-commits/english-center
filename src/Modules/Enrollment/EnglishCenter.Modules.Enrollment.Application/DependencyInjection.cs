using Microsoft.Extensions.DependencyInjection;
using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Application.Registrations;
using EnglishCenter.Modules.Enrollment.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;

namespace EnglishCenter.Modules.Enrollment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEnrollmentApplication(this IServiceCollection services)
    {
        services.AddScoped<Enrollments.EnrollmentService>();
        services.AddScoped<EnglishCenter.Modules.Enrollment.Contracts.IEnrollmentAccess>(sp => sp.GetRequiredService<Enrollments.EnrollmentService>());
        services.AddScoped<EnrollmentService>();
        services.AddIntegrationEventHandler<UserRegisteredIntegrationEvent, UserRegisteredHandler>();
        services.AddIntegrationEventHandler<ClassCreatedIntegrationEvent, ClassCreatedHandler>();
        return services;
    }
}
