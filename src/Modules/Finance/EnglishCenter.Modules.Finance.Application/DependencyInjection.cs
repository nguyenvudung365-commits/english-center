using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers;
using Microsoft.Extensions.DependencyInjection;
using EnglishCenter.Modules.Finance.Application.Invoices;

namespace EnglishCenter.Modules.Finance.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceApplication(this IServiceCollection services)
    {
        services.AddScoped<InvoiceQuery>();
        services.AddScoped<InvoiceManagement>();
        services.AddIntegrationEventHandler<StudentEnrolledIntegrationEvent, StudentEnrolledIntegrationEventHandler>();
        return services;
    }
}
