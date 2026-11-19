using EnglishCenter.Modules.Academic.Domain.Assignments;
using EnglishCenter.Modules.Academic.Domain.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class ClassMaterialConfiguration : IEntityTypeConfiguration<ClassMaterial>
{
    public void Configure(EntityTypeBuilder<ClassMaterial> builder)
    {
        builder.ToTable("class_materials", t => t.HasCheckConstraint("ck_class_materials_size", "size_bytes > 0 AND size_bytes <= 20971520"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(ClassMaterial.TitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(ClassMaterial.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        // FileId là tham chiếu logic tới Media; cố ý không có FK chéo schema. Một file chỉ đăng được một lần.
        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasIndex(x => new { x.ClassId, x.CreatedAt });
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}
