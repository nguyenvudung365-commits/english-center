using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<Authentication.IdentityService>();
        services.AddScoped<Authentication.UserSyncService>();
        services.AddScoped<EnglishCenter.Modules.Identity.Contracts.IStudentDirectory, Authentication.StudentDirectory>();
        services.AddScoped<EnglishCenter.Modules.Identity.Contracts.IStudentDisplayDirectory, Authentication.StudentDisplayDirectory>();
        services.AddScoped<EnglishCenter.Modules.Identity.Contracts.IUserDisplayDirectory, Authentication.UserDisplayDirectory>();
        services.AddScoped<EnglishCenter.Modules.Identity.Contracts.IAccountAccess, Authentication.AccountAccess>();
        return services;
    }
}
