using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

public sealed class EnrollmentDbContext : DbContext
{
    public DbSet<EnglishCenter.Modules.Enrollment.Domain.Enrollments.ClassEnrollment> Enrollments => Set<EnglishCenter.Modules.Enrollment.Domain.Enrollments.ClassEnrollment>();
    public EnrollmentDbContext(DbContextOptions<EnrollmentDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EnrollmentSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EnrollmentDbContext).Assembly);
    }
}
