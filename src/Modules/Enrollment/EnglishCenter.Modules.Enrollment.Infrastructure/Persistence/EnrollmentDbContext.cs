using Microsoft.EntityFrameworkCore;
using EnglishCenter.Modules.Enrollment.Domain.Registrations;
using EnglishCenter.Modules.Enrollment.Domain.PlacementTests;

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;

public sealed class EnrollmentDbContext : DbContext
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<ClassSnapshot> Classes => Set<ClassSnapshot>();
    public DbSet<Registration> Enrollments => Set<Registration>();
    public DbSet<AttendanceSnapshot> AttendanceSnapshots => Set<AttendanceSnapshot>();
    public DbSet<MissedAssignment> MissedAssignments => Set<MissedAssignment>();
    public DbSet<Warning> Warnings => Set<Warning>();
    public DbSet<PlacementTest> PlacementTests => Set<PlacementTest>();
    public DbSet<TestTeacher> TestTeachers => Set<TestTeacher>();
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
            b.Property(x => x.EnrolledAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        });
        modelBuilder.Entity<AttendanceSnapshot>(b =>
        {
            b.ToTable("attendance_snapshots", t => t.HasCheckConstraint("ck_attendance_status",
                "status IN ('Present', 'AbsentUnexcused', 'AbsentExcused', 'Late', 'LateOver20Min', 'LeftEarly')"));
            b.HasKey(x => new { x.SessionId, x.StudentId });
            b.HasOne<Registration>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Status).HasMaxLength(30);
            b.Property(x => x.RecordedAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        });
        modelBuilder.Entity<MissedAssignment>(b =>
        {
            b.ToTable("missed_assignments"); b.HasKey(x => new { x.AssignmentId, x.EnrollmentId });
            b.HasOne<Registration>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.RecordedAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        });
        modelBuilder.Entity<Warning>(b =>
        {
            b.ToTable("warnings", t => t.HasCheckConstraint("ck_warning_type", "warning_type IN ('Absence', 'Late', 'SkippedAssignment')"));
            b.HasKey(x => x.Id); b.HasIndex(x => new { x.EnrollmentId, x.WarningType }).IsUnique();
            b.HasOne<Registration>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.WarningType).HasMaxLength(30); b.Property(x => x.Message).IsRequired();
            b.Property(x => x.IssuedAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        });
        modelBuilder.Entity<TestTeacher>(b => { b.ToTable("test_teachers"); b.HasKey(x => x.TeacherId); });
        modelBuilder.Entity<PlacementTest>(b =>
        {
            b.ToTable("placement_tests", t =>
            {
                t.HasCheckConstraint("ck_placement_mode", "mode IN ('Offline', 'Online')");
                t.HasCheckConstraint("ck_placement_status", "status IN ('Pending', 'Done', 'Cancelled')");
            });
            b.HasKey(x => x.Id); b.HasIndex(x => new { x.StudentId, x.ScheduledAt });
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<TestTeacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Location).HasMaxLength(200); b.Property(x => x.TestName).HasMaxLength(200);
            b.Property(x => x.Mode).HasMaxLength(20); b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.ScheduledAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
            b.Property(x => x.CreatedAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        });
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
                p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}
