using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.MediaNotification.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaNotificationApplication(this IServiceCollection services)
    {
        services.AddScoped<EnglishCenter.Modules.MediaNotification.Contracts.ISubmissionFileAccess, Files.SubmissionFileAccess>();
        services.AddScoped<EnglishCenter.Modules.MediaNotification.Contracts.IAvatarFileAccess, Files.AvatarFileAccess>();
        services.AddScoped<Files.FileService>();
        return services;
    }
}
