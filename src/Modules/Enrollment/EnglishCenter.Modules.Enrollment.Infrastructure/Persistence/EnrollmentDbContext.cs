using Microsoft.EntityFrameworkCore;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

public sealed class EnrollmentDbContext : DbContext
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<ClassSnapshot> Classes => Set<ClassSnapshot>();
    public DbSet<Registration> Enrollments => Set<Registration>();
    public EnrollmentDbContext(DbContextOptions<EnrollmentDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EnrollmentSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EnrollmentDbContext).Assembly);
        modelBuilder.Entity<Student>(b =>
        {
            b.ToTable("students"); b.HasKey(x => x.StudentId);
            b.Property(x => x.FullName).HasMaxLength(200); b.Property(x => x.Email).HasMaxLength(255);
        });
        modelBuilder.Entity<ClassSnapshot>(b =>
        {
            b.ToTable("class_snapshots", t =>
            {
                t.HasCheckConstraint("ck_class_dates", "end_date > start_date");
                t.HasCheckConstraint("ck_class_capacity", "max_students > 0 AND current_students >= 0 AND current_students <= max_students");
                t.HasCheckConstraint("ck_class_tuition", "tuition_fee >= 0");
            });
            b.HasKey(x => x.ClassId); b.Property(x => x.ClassCode).HasMaxLength(50);
            b.Property(x => x.TuitionFee).HasPrecision(12, 0);
        });
        modelBuilder.Entity<Registration>(b =>
        {
            b.ToTable("enrollments", t =>
            {
                t.HasCheckConstraint("ck_enrollment_limits", "max_absences >= 0 AND max_lates >= 0 AND max_skipped_assignments >= 0 AND absence_count >= 0 AND late_count >= 0 AND skipped_assignment_count >= 0");
                t.HasCheckConstraint("ck_enrollment_status", "status IN ('Active', 'Cancelled')");
                t.HasCheckConstraint("ck_enrollment_tuition", "tuition_fee >= 0");
            });
            b.HasKey(x => x.Id); b.HasIndex(x => new { x.StudentId, x.ClassId }).IsUnique();
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ClassSnapshot>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.TuitionFee).HasPrecision(12, 0);
            b.Property(x => x.IsActive).HasDefaultValue(true);
            b.Property(x => x.EnrolledAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}
