using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Courses;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("classes", table =>
        {
            table.HasCheckConstraint("ck_classes_end_date_after_start_date", "end_date > start_date");
            table.HasCheckConstraint("ck_classes_max_students_positive", "max_students > 0");
            table.HasCheckConstraint("ck_classes_tuition_fee_non_negative", "tuition_fee >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(Class.CodeMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(Class.NameMaxLength).IsRequired();
        builder.Property(x => x.ClassType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.TuitionFee).HasPrecision(12, 0);
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
    }
}
