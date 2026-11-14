using EnglishCenter.Modules.Academic.Application.Abstractions;
using EnglishCenter.Modules.Academic.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EnglishCenter.Modules.Academic.Application.Classes;

namespace EnglishCenter.Modules.Academic.Infrastructure;

public static class DependencyInjection
{
    // Chuỗi cục bộ không có mật khẩu, chỉ để host khởi động được khi chưa cấu hình ConnectionStrings__Default.
    private const string FallbackConnectionString = "Host=localhost;Database=english_center";

    public static IServiceCollection AddAcademicInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = FallbackConnectionString;
        }

        // Không kết nối DB khi khởi động; chỉ cấu hình provider.
        services.AddDbContext<AcademicDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AcademicSchema.Name)));

        services.AddSingleton<IAcademicModuleInfo, AcademicModuleInfo>();
        services.AddScoped<IAcademicStore, AcademicStore>();
        services.AddScoped<EnglishCenter.Modules.Academic.Application.Assignments.IAssignmentStore, AssignmentStore>();
        var maxFiles = configuration.GetValue<int?>("Academic:Submissions:MaxFiles") ?? 5;
        var maxTotalBytes = configuration.GetValue<long?>("Academic:Submissions:MaxTotalBytes") ?? 50L * 1024 * 1024;
        if (maxFiles is < 1 or > 5 || maxTotalBytes is < 1 or > 50L * 1024 * 1024)
            throw new InvalidOperationException("Submission limits must be 1–5 files and 1–52428800 total bytes.");
        services.AddSingleton(new EnglishCenter.Modules.Academic.Application.Assignments.SubmissionLimits(maxFiles, maxTotalBytes));
        services.AddScoped<IClassRoster, ClassRoster>();
        services.AddScoped<EnglishCenter.Modules.Academic.Application.Practice.IPracticeStore, PracticeStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<AcademicStartup>();
        services.AddHostedService<CourseworkWorker>();

        return services;
    }
}
