using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

internal sealed class EnrollmentStartup(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!bool.TryParse(configuration["Enrollment:InitializeDatabase"], out var initialize) || !initialize) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EnrollmentDbContext>().Database.MigrateAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
