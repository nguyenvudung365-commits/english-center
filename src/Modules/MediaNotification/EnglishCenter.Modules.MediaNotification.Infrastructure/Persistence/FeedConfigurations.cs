using EnglishCenter.Modules.MediaNotification.Domain.Feed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class ClassPostConfiguration : IEntityTypeConfiguration<ClassPost>
{
    public void Configure(EntityTypeBuilder<ClassPost> builder)
    {
        builder.ToTable("class_posts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AuthorRole).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.State).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.ClassId, x.CreatedAt });
    }
}

internal sealed class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        builder.ToTable("post_comments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AuthorRole).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.State).HasMaxLength(16).IsRequired();
        builder.HasOne<ClassPost>().WithMany().HasForeignKey(x => x.PostId);
        builder.HasIndex(x => new { x.PostId, x.CreatedAt });
    }
}

internal sealed class PostLikeConfiguration : IEntityTypeConfiguration<PostLike>
{
    public void Configure(EntityTypeBuilder<PostLike> builder)
    {
        builder.ToTable("post_likes");
        builder.HasKey(x => new { x.PostId, x.UserId });
        builder.HasOne<ClassPost>().WithMany().HasForeignKey(x => x.PostId);
    }
}
