using EnglishCenter.Modules.MediaNotification.Domain.Faqs;
using EnglishCenter.Modules.MediaNotification.Domain.Feedback;
using EnglishCenter.Modules.MediaNotification.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.RecipientId, x.Kind, x.SourceId, x.SourceVersion }).IsUnique();
        builder.HasIndex(x => new { x.RecipientId, x.CreatedAt });
    }
}

internal sealed class FeedbackConfiguration : IEntityTypeConfiguration<StudentFeedback>
{
    public void Configure(EntityTypeBuilder<StudentFeedback> builder)
    {
        builder.ToTable("feedbacks", t => t.HasCheckConstraint("CK_feedbacks_rating", "\"Rating\" BETWEEN 1 AND 5"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.StudentId, x.CreatedAt });
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

internal sealed class FaqConfiguration : IEntityTypeConfiguration<Faq>
{
    public void Configure(EntityTypeBuilder<Faq> builder)
    {
        builder.ToTable("faqs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Question).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Answer).HasMaxLength(4000).IsRequired();
        builder.HasIndex(x => new { x.IsPublished, x.CreatedAt });
    }
}
