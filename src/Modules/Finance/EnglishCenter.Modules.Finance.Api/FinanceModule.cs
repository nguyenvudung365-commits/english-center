using EnglishCenter.Modules.Finance.Application;
using EnglishCenter.Modules.Finance.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Finance.Api;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddFinanceApplication();
        services.AddFinanceInfrastructure(configuration);
        return services;
    }
}
