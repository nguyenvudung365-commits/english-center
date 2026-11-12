using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Application.IntegrationEventHandlers;
using EnglishCenter.Modules.Identity.Contracts.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Academic.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicApplication(this IServiceCollection services)
    {
        services.AddScoped<ClassService>();
        services.AddScoped<EnglishCenter.Modules.Academic.Contracts.IClassDirectory, ClassDirectory>();
        services.AddScoped<Assignments.AssignmentService>();
        services.AddIntegrationEventHandler<UserRegisteredIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        services.AddIntegrationEventHandler<UserProfileUpdatedIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        return services;
    }
}
