using EnglishCenter.Modules.MediaNotification.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("stored_files");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(x => x.StagingKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.FileKey).HasMaxLength(256);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.StagingKey).IsUnique();
        builder.HasIndex(x => x.FileKey).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.Id });
        builder.HasIndex(x => new { x.OwnerId, x.UploadedAt });
    }
}
