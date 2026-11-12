using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

public sealed class MediaNotificationDesignTimeFactory : IDesignTimeDbContextFactory<MediaNotificationDbContext>
{
    public MediaNotificationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=english_center";
        var options = new DbContextOptionsBuilder<MediaNotificationDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", MediaNotificationSchema.Name)).Options;
        return new MediaNotificationDbContext(options);
    }
}
