using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

public sealed class AcademicDesignTimeFactory : IDesignTimeDbContextFactory<AcademicDbContext>
{
    public AcademicDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=english_center";
        var options = new DbContextOptionsBuilder<AcademicDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", AcademicSchema.Name)).Options;
        return new AcademicDbContext(options);
    }
}
