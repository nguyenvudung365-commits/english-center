using EnglishCenter.Modules.Enrollment.Application;
using EnglishCenter.Modules.Enrollment.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Enrollment.Api;

public static class EnrollmentModule
{
    public static IServiceCollection AddEnrollmentModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEnrollmentApplication();
        services.AddEnrollmentInfrastructure(configuration);
        return services;
    }
}
