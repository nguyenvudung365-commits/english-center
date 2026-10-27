using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

public sealed class MediaNotificationDbContext : DbContext
{
    public MediaNotificationDbContext(DbContextOptions<MediaNotificationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MediaNotificationSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaNotificationDbContext).Assembly);
    }
}
