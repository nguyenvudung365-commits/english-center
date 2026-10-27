using EnglishCenter.Modules.Academic.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    // Backlog chưa có story quản lý khóa học; seed danh mục mẫu để tạo lớp (US-07) dùng được ngay.
    public static readonly Guid IeltsFoundationId = Guid.Parse("0c1b6a52-3f4e-4d0a-9b7e-1a2f3c4d5e01");
    public static readonly Guid IeltsIntermediateId = Guid.Parse("0c1b6a52-3f4e-4d0a-9b7e-1a2f3c4d5e02");
    public static readonly Guid IeltsAdvancedId = Guid.Parse("0c1b6a52-3f4e-4d0a-9b7e-1a2f3c4d5e03");

    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(Course.CodeMaxLength).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(Course.NameMaxLength).IsRequired();
        builder.Property(x => x.Description);

        builder.HasData(
            Course.Create(IeltsFoundationId, "IELTS-FOUNDATION", "IELTS Foundation", "Nền tảng 4 kỹ năng, mục tiêu band 4.0-4.5"),
            Course.Create(IeltsIntermediateId, "IELTS-INTERMEDIATE", "IELTS Intermediate", "Mục tiêu band 5.0-5.5"),
            Course.Create(IeltsAdvancedId, "IELTS-ADVANCED", "IELTS Advanced", "Mục tiêu band 6.5 trở lên"));
    }
}
