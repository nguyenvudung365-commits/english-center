using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Finance.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceApplication(this IServiceCollection services)
    {
        services.AddIntegrationEventHandler<StudentEnrolledIntegrationEvent, StudentEnrolledIntegrationEventHandler>();
        return services;
    }
}
