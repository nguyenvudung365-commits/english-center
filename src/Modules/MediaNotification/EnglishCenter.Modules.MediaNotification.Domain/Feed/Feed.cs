namespace EnglishCenter.Modules.MediaNotification.Domain.Feed;

public static class FeedState
{
    public const string Visible = "Visible";
    public const string Hidden = "Hidden";
    public const string Removed = "Removed";
}

// Bài đăng và bình luận dùng chung: xóa/ẩn là chuyển trạng thái để còn dấu vết người thực hiện.
public abstract class FeedContent
{
    public Guid Id { get; protected set; }
    public Guid ClassId { get; protected set; }
    public Guid AuthorId { get; protected set; }
    public string AuthorRole { get; protected set; } = string.Empty;
    public string Content { get; protected set; } = string.Empty;
    public string State { get; protected set; } = FeedState.Visible;
    public DateTime CreatedAt { get; protected set; }
    public DateTime? UpdatedAt { get; protected set; }
    public Guid? ModeratedBy { get; protected set; }
    public DateTime? ModeratedAt { get; protected set; }

    public void Edit(string content, DateTime now)
    {
        Content = content;
        UpdatedAt = now;
    }

    public void SetHidden(bool hidden, Guid actor, DateTime now) => Moderate(hidden ? FeedState.Hidden : FeedState.Visible, actor, now);

    public void Remove(Guid actor, DateTime now) => Moderate(FeedState.Removed, actor, now);

    private void Moderate(string state, Guid actor, DateTime now)
    {
        State = state;
        ModeratedBy = actor;
        ModeratedAt = now;
    }
}

public sealed class ClassPost : FeedContent
{
    private ClassPost() { }

    public static ClassPost Create(Guid classId, Guid authorId, string authorRole, string content, DateTime now) => new()
    {
        Id = Guid.NewGuid(), ClassId = classId, AuthorId = authorId, AuthorRole = authorRole, Content = content, CreatedAt = now
    };
}

public sealed class PostComment : FeedContent
{
    private PostComment() { }

    public Guid PostId { get; private set; }

    public static PostComment Create(ClassPost post, Guid authorId, string authorRole, string content, DateTime now) => new()
    {
        Id = Guid.NewGuid(), PostId = post.Id, ClassId = post.ClassId, AuthorId = authorId, AuthorRole = authorRole,
        Content = content, CreatedAt = now
    };
}

public sealed class PostLike
{
    private PostLike() { }

    public Guid PostId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static PostLike Create(Guid postId, Guid userId, DateTime now) => new() { PostId = postId, UserId = userId, CreatedAt = now };
}
