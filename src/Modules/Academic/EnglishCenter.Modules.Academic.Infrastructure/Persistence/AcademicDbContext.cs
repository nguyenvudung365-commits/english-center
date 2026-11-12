using System.Text;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence;

public sealed class AcademicDbContext : DbContext
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Class> Classes => Set<Class>();
    public DbSet<EnglishCenter.Modules.Academic.Domain.Assignments.Assignment> Assignments => Set<EnglishCenter.Modules.Academic.Domain.Assignments.Assignment>();
    public DbSet<EnglishCenter.Modules.Academic.Domain.Assignments.Submission> Submissions => Set<EnglishCenter.Modules.Academic.Domain.Assignments.Submission>();
    public DbSet<ClassStudent> ClassStudents => Set<ClassStudent>();

    public AcademicDbContext(DbContextOptions<AcademicDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AcademicSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcademicDbContext).Assembly);
        modelBuilder.Entity<ClassStudent>(b =>
        {
            b.ToTable("class_students"); b.HasKey(x => x.EnrollmentId);
            b.HasIndex(x => new { x.StudentId, x.ClassId }).IsUnique();
            b.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        });

        // Tài liệu 04: tên cột snake_case (CourseId -> course_id).
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) builder.Append('_');
            builder.Append(char.ToLowerInvariant(name[i]));
        }
        return builder.ToString();
    }
}
