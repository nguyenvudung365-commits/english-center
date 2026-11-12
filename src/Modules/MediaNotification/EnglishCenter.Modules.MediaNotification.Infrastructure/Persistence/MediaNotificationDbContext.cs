using Microsoft.EntityFrameworkCore;
using EnglishCenter.Modules.MediaNotification.Domain.Files;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

public sealed class MediaNotificationDbContext : DbContext
{
    public MediaNotificationDbContext(DbContextOptions<MediaNotificationDbContext> options) : base(options)
    {
    }

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MediaNotificationSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaNotificationDbContext).Assembly);
    }
}
