using EnglishCenter.Modules.Academic.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    // Backlog chưa có story quản lý khóa học; seed danh mục mẫu để tạo lớp (US-07) dùng được ngay.
    public static readonly Guid IeltsFoundationId = Guid.Parse("34d802e7-5319-446a-9104-c6ff10cd6a00");
    public static readonly Guid IeltsIntermediateId = Guid.Parse("1ae06bfd-eed6-476b-b63d-ef71138ee3b0");
    public static readonly Guid IeltsAdvancedId = Guid.Parse("3dd0ae5a-0547-42dc-b834-9f4b0d502752");

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
