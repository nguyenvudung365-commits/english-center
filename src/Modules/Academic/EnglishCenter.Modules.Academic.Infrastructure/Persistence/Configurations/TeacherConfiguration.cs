using EnglishCenter.Modules.Academic.Domain.Teachers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> builder)
    {
        builder.ToTable("teachers");
        builder.HasKey(x => x.Id);
        // Id = identity.users.id (tham chiếu logic, không FK chéo schema).
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FullName).HasMaxLength(Teacher.FullNameMaxLength).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(Teacher.EmailMaxLength).IsRequired();
    }
}
