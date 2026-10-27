using EnglishCenter.Modules.Identity.Application.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnglishCenter.Modules.Identity.Infrastructure.Persistence;

internal sealed class IdentityStartup(IServiceScopeFactory scopes, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!bool.TryParse(configuration["Identity:InitializeDatabase"], out var initialize) || !initialize) return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(ct);
        var email = configuration["Identity:BootstrapAdmin:Email"];
        if (string.IsNullOrWhiteSpace(email)) return;
        await scope.ServiceProvider.GetRequiredService<IdentityService>().BootstrapAdminAsync(email,
            configuration["Identity:BootstrapAdmin:FullName"] ?? "Administrator",
            configuration["Identity:BootstrapAdmin:Password"] ?? string.Empty, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
