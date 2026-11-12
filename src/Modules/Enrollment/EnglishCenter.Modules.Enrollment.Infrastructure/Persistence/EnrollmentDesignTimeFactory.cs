using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

public sealed class EnrollmentDesignTimeFactory : IDesignTimeDbContextFactory<EnrollmentDbContext>
{
    public EnrollmentDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=english_center";
        var options = new DbContextOptionsBuilder<EnrollmentDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", EnrollmentSchema.Name)).Options;
        return new EnrollmentDbContext(options);
    }
}
