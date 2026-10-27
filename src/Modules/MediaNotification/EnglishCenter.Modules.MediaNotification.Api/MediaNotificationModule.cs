using EnglishCenter.Modules.MediaNotification.Application;
using EnglishCenter.Modules.MediaNotification.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.MediaNotification.Api;

public static class MediaNotificationModule
{
    public static IServiceCollection AddMediaNotificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediaNotificationApplication();
        services.AddMediaNotificationInfrastructure(configuration);
        return services;
    }
}
