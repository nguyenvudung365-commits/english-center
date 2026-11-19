using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Practice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Configurations;

internal sealed class PracticeConfiguration : IEntityTypeConfiguration<PracticeExercise>
{
    public void Configure(EntityTypeBuilder<PracticeExercise> builder)
    {
        builder.ToTable("practice_exercises");
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(PracticeExercise.TitleMaxLength).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(PracticeExercise.ContentMaxLength).IsRequired();
        builder.Property(x => x.CreatedByRole).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.ClassId);
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }
}
