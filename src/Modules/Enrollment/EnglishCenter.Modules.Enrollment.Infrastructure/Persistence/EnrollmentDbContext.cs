using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

public sealed class EnrollmentDbContext : DbContext
{
    public EnrollmentDbContext(DbContextOptions<EnrollmentDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EnrollmentSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EnrollmentDbContext).Assembly);
    }
}
