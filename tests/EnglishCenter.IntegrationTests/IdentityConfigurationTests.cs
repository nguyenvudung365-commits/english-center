using EnglishCenter.Modules.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class IdentityConfigurationTests
{
    [Fact]
    public void PostgreSqlMigration_OnlyTouchesIdentitySchema()
    {
        using var db = new IdentityDesignTimeFactory().CreateDbContext([]);
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE identity.users", script);
        Assert.Contains("CREATE TABLE identity.refresh_sessions", script);
        Assert.Contains("identity.\"__EFMigrationsHistory\"", script);
        Assert.Contains("UNIQUE INDEX", script);
        Assert.DoesNotContain("academic.", script);
        Assert.DoesNotContain("enrollment.", script);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Production_RejectsMissingSigningKey()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "",
                ["Identity:InitializeDatabase"] = "false"
            }));
        });
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
