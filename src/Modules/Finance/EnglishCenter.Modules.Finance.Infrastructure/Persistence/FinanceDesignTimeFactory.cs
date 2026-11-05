using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDesignTimeFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=english_center";
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", FinanceSchema.Name)).Options;
        return new FinanceDbContext(options);
    }
}
