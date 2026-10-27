using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Academic.Api;

public static class AcademicModule
{
    public static IServiceCollection AddAcademicModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAcademicApplication();
        services.AddAcademicInfrastructure(configuration);
        return services;
    }
}
