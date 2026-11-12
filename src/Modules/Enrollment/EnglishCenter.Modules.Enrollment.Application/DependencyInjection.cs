using Microsoft.Extensions.DependencyInjection;

namespace EnglishCenter.Modules.Enrollment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEnrollmentApplication(this IServiceCollection services)
    {
        services.AddScoped<Enrollments.EnrollmentService>();
        services.AddScoped<EnglishCenter.Modules.Enrollment.Contracts.IEnrollmentAccess>(sp => sp.GetRequiredService<Enrollments.EnrollmentService>());
        return services;
    }
}
