using EnglishCenter.Modules.Identity.Application.Abstractions;
using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

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
        services.AddScoped<IIdentityStore, IdentityStore>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddSingleton(TimeProvider.System);

        var jwt = new JwtOptions();
        configuration.GetSection("Jwt").Bind(jwt);
        var environment = configuration[HostDefaults.EnvironmentKey] ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) && environment == Environments.Development)
            jwt.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        services.AddOptions<JwtOptions>().Configure(o =>
        {
            o.SigningKey = jwt.SigningKey; o.Issuer = jwt.Issuer; o.Audience = jwt.Audience;
            o.AccessTokenMinutes = jwt.AccessTokenMinutes; o.RefreshTokenDays = jwt.RefreshTokenDays;
        }).Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey must have at least 32 UTF-8 bytes.")
          .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "JWT issuer and audience are required.")
          .Validate(o => o.AccessTokenMinutes is > 0 and <= 60 && o.RefreshTokenDays is > 0 and <= 30, "Invalid JWT lifetimes.")
          .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = AuthClaims.Subject, RoleClaimType = AuthClaims.Role, ClockSkew = TimeSpan.FromSeconds(30)
            };
        });
        services.AddAuthorization();
        services.AddHostedService<IdentityStartup>();

        return services;
    }
}
