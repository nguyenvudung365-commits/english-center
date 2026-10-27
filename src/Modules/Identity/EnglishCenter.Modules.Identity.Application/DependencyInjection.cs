using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<Authentication.IdentityService>();
        return services;
    }
}
