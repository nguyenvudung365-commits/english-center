using EnglishCenter.Modules.MediaNotification.Domain.Faqs;
using EnglishCenter.Modules.MediaNotification.Domain.Feed;
using EnglishCenter.Modules.MediaNotification.Domain.Feedback;
using EnglishCenter.Modules.MediaNotification.Domain.Files;
using EnglishCenter.Modules.MediaNotification.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

public sealed class MediaNotificationDbContext : DbContext
{
    public MediaNotificationDbContext(DbContextOptions<MediaNotificationDbContext> options) : base(options)
    {
    }

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<StudentFeedback> Feedbacks => Set<StudentFeedback>();
    public DbSet<Faq> Faqs => Set<Faq>();
    public DbSet<ClassPost> Posts => Set<ClassPost>();
    public DbSet<PostComment> Comments => Set<PostComment>();
    public DbSet<PostLike> Likes => Set<PostLike>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MediaNotificationSchema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaNotificationDbContext).Assembly);
    }
}
