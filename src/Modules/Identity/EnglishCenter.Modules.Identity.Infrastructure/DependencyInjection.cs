using EnglishCenter.Modules.Identity.Application.Abstractions;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Identity.Infrastructure;

public static class DependencyInjection
{
    // Chuỗi cục bộ không có mật khẩu, chỉ để host khởi động được khi chưa cấu hình ConnectionStrings__Default.
    private const string FallbackConnectionString = "Host=localhost;Database=english_center";

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = FallbackConnectionString;
        }

        // Không kết nối DB khi khởi động; chỉ cấu hình provider.
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentitySchema.Name)));

        services.AddSingleton<IIdentityModuleInfo, IdentityModuleInfo>();

        return services;
    }
}
