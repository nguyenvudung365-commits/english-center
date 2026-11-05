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
        services.AddIntegrationEventHandler<UserRegisteredIntegrationEvent, UserRegisteredIntegrationEventHandler>();
        return services;
    }
}
