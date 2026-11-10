using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

internal sealed class FinanceStartup(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!bool.TryParse(configuration["Finance:InitializeDatabase"], out var initialize) || !initialize) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.MigrateAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
