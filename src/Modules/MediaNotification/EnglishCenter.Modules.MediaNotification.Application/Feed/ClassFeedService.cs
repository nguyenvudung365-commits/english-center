using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;
using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Domain.Feed;

namespace EnglishCenter.Modules.MediaNotification.Application.Feed;

public sealed record ContentRequest(string? Content);
public sealed record HiddenRequest(bool? Hidden);
public sealed record PostResponse(Guid Id, Guid ClassId, Guid AuthorId, string AuthorRole, string? AuthorName, string Content,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool IsHidden, int LikeCount, bool LikedByMe, int CommentCount);
public sealed record CommentResponse(Guid Id, Guid PostId, Guid AuthorId, string AuthorRole, string? AuthorName, string Content,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, bool IsHidden);
public sealed record LikeResponse(int LikeCount, bool LikedByMe);
public sealed record PostView(ClassPost Post, int Likes, bool LikedByMe, int Comments);

public interface IClassFeedStore
{
    Task AddAsync(ClassPost post, CancellationToken ct);
    Task AddAsync(PostComment comment, CancellationToken ct);
    Task<ClassPost?> FindPostAsync(Guid id, CancellationToken ct);
    Task<PostComment?> FindCommentAsync(Guid id, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    // Đếm cả bài đã ẩn/xóa để không lách giới hạn bằng cách xóa bài.
    Task<int> CountByAuthorSinceAsync(Guid author, DateTime since, CancellationToken ct);
    Task<PagedResult<PostView>> PostsAsync(Guid classId, bool includeHidden, Guid viewer, int page, int size, CancellationToken ct);
    Task<PagedResult<PostComment>> CommentsAsync(Guid postId, bool includeHidden, int page, int size, CancellationToken ct);
    // Like/unlike idempotent (khóa duy nhất PostId+UserId); trả số like hiện tại, không bao giờ âm.
    Task<int> SetLikeAsync(Guid postId, Guid user, bool liked, DateTime now, CancellationToken ct);
}

// Đọc/đăng: Student còn ghi danh active, Teacher đúng lớp, Staff/Admin. Mỗi request kiểm tra lại quyền lớp hiện tại.
public sealed class ClassFeedService(IClassFeedStore store, MediaAccess access, IClassDirectory classes,
    IEnrollmentAccess enrollment, IUserDisplayDirectory users, TimeProvider clock)
{
    // shortcut: đếm rồi mới ghi nên request song song có thể vượt giới hạn vài bản ghi; cần khóa/bộ đếm nguyên tử nếu muốn chặt hơn.
    private const int HourlyLimit = 30;

    private sealed record Scope(Actor Actor, bool Moderator);

    public async Task<PagedResult<PostResponse>> PostsAsync(Guid classId, int page, int size, CancellationToken ct)
    {
        var scope = await ScopeAsync(await ActorAsync(ct), classId, hideDenied: false, ct);
        (page, size) = Paging.Validate(page, size);
        var result = await store.PostsAsync(classId, scope.Moderator, scope.Actor.Id, page, size, ct);
        var names = await users.FindNamesAsync(result.Items.Select(x => x.Post.AuthorId).ToList(), ct);
        return new(result.Items.Select(x => Response(x, names.GetValueOrDefault(x.Post.AuthorId))).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<PostResponse> CreatePostAsync(Guid classId, ContentRequest request, CancellationToken ct)
    {
        var scope = await ScopeAsync(await ActorAsync(ct), classId, hideDenied: false, ct);
        var content = Content(request, 4000);
        await RequireWithinLimitAsync(scope.Actor.Id, ct);
        var post = ClassPost.Create(classId, scope.Actor.Id, scope.Actor.Role, content, Now());
        await store.AddAsync(post, ct);
        return Response(new PostView(post, 0, false, 0), await NameAsync(post.AuthorId, ct));
    }

    public async Task EditPostAsync(Guid id, ContentRequest request, CancellationToken ct)
    {
        var (post, scope) = await PostAsync(id, ct);
        RequireAuthor(post, scope);
        post.Edit(Content(request, 4000), Now());
        await store.SaveAsync(ct);
    }

    public async Task RemovePostAsync(Guid id, CancellationToken ct)
    {
        var (post, scope) = await PostAsync(id, ct);
        if (!scope.Moderator) RequireAuthor(post, scope);
        post.Remove(scope.Actor.Id, Now());
        await store.SaveAsync(ct);
    }

    public async Task SetPostHiddenAsync(Guid id, HiddenRequest request, CancellationToken ct)
    {
        var (post, scope) = await PostAsync(id, ct);
        RequireModerator(scope);
        post.SetHidden(request.Hidden ?? throw new MediaFailure(400, "hidden is required."), scope.Actor.Id, Now());
        await store.SaveAsync(ct);
    }

    public async Task<LikeResponse> SetLikeAsync(Guid id, bool liked, CancellationToken ct)
    {
        var (post, scope) = await PostAsync(id, ct);
        return new(await store.SetLikeAsync(post.Id, scope.Actor.Id, liked, Now(), ct), liked);
    }

    public async Task<PagedResult<CommentResponse>> CommentsAsync(Guid postId, int page, int size, CancellationToken ct)
    {
        var (post, scope) = await PostAsync(postId, ct);
        (page, size) = Paging.Validate(page, size);
        var result = await store.CommentsAsync(post.Id, scope.Moderator, page, size, ct);
        var names = await users.FindNamesAsync(result.Items.Select(x => x.AuthorId).ToList(), ct);
        return new(result.Items.Select(x => Response(x, names.GetValueOrDefault(x.AuthorId))).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<CommentResponse> CreateCommentAsync(Guid postId, ContentRequest request, CancellationToken ct)
    {
        var (post, scope) = await PostAsync(postId, ct);
        var content = Content(request, 1000);
        await RequireWithinLimitAsync(scope.Actor.Id, ct);
        var comment = PostComment.Create(post, scope.Actor.Id, scope.Actor.Role, content, Now());
        await store.AddAsync(comment, ct);
        return Response(comment, await NameAsync(comment.AuthorId, ct));
    }

    public async Task<CommentResponse> EditCommentAsync(Guid id, ContentRequest request, CancellationToken ct)
    {
        var (comment, scope) = await CommentAsync(id, ct);
        RequireAuthor(comment, scope);
        comment.Edit(Content(request, 1000), Now());
        await store.SaveAsync(ct);
        return Response(comment, await NameAsync(comment.AuthorId, ct));
    }

    public async Task RemoveCommentAsync(Guid id, CancellationToken ct)
    {
        var (comment, scope) = await CommentAsync(id, ct);
        if (!scope.Moderator) RequireAuthor(comment, scope);
        comment.Remove(scope.Actor.Id, Now());
        await store.SaveAsync(ct);
    }

    public async Task SetCommentHiddenAsync(Guid id, HiddenRequest request, CancellationToken ct)
    {
        var (comment, scope) = await CommentAsync(id, ct);
        RequireModerator(scope);
        comment.SetHidden(request.Hidden ?? throw new MediaFailure(400, "hidden is required."), scope.Actor.Id, Now());
        await store.SaveAsync(ct);
    }

    // Route theo lớp: lớp không tồn tại → 404, không có quyền → 403 (cùng mẫu ClassAccess). Route theo post/comment:
    // người ngoài lớp nhận 404 để không lộ sự tồn tại của ID đoán được.
    private async Task<Scope> ScopeAsync(Actor actor, Guid classId, bool hideDenied, CancellationToken ct)
    {
        var item = await classes.FindClassAsync(classId, ct) ?? throw new MediaFailure(404, "Class not found.");
        var moderator = actor.IsManager || (actor.Role == Roles.Teacher && item.TeacherId == actor.Id);
        if (!moderator && !(actor.Role == Roles.Student && await enrollment.IsActiveAsync(classId, actor.Id, ct)))
            throw hideDenied ? new MediaFailure(404, "Not found.") : new MediaFailure(403, "You do not have access to this class feed.");
        return new(actor, moderator);
    }

    private async Task<(ClassPost, Scope)> PostAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var post = await store.FindPostAsync(id, ct) ?? throw new MediaFailure(404, "Not found.");
        var scope = await ScopeAsync(actor, post.ClassId, hideDenied: true, ct);
        return (Live(post, scope), scope);
    }

    private async Task<(PostComment, Scope)> CommentAsync(Guid id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var comment = await store.FindCommentAsync(id, ct) ?? throw new MediaFailure(404, "Not found.");
        var scope = await ScopeAsync(actor, comment.ClassId, hideDenied: true, ct);
        return (Live(comment, scope), scope);
    }

    // Đã xóa thì không ai thấy; đã ẩn thì chỉ người kiểm duyệt thấy.
    private static T Live<T>(T item, Scope scope) where T : FeedContent =>
        item.State == FeedState.Visible || (item.State == FeedState.Hidden && scope.Moderator) ? item : throw new MediaFailure(404, "Not found.");

    private static void RequireAuthor(FeedContent item, Scope scope)
    {
        if (item.AuthorId != scope.Actor.Id) throw new MediaFailure(403, "Only the author can change this content.");
    }

    private static void RequireModerator(Scope scope)
    {
        if (!scope.Moderator) throw new MediaFailure(403, "Only the class teacher, staff or admin can moderate.");
    }

    private Task<Actor> ActorAsync(CancellationToken ct) => access.ActiveAsync(ct, MediaAccess.Everyone);
    private static string Content(ContentRequest request, int max) => Text.Clean(request.Content, max, "Content", multiline: true);
    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private async Task RequireWithinLimitAsync(Guid author, CancellationToken ct)
    {
        if (await store.CountByAuthorSinceAsync(author, Now().AddHours(-1), ct) >= HourlyLimit)
            throw new MediaFailure(429, "Too many posts and comments; try again later.");
    }

    private async Task<string?> NameAsync(Guid id, CancellationToken ct) => (await users.FindNamesAsync([id], ct)).GetValueOrDefault(id);

    private static PostResponse Response(PostView x, string? name) => new(x.Post.Id, x.Post.ClassId, x.Post.AuthorId, x.Post.AuthorRole,
        name, x.Post.Content, x.Post.CreatedAt, x.Post.UpdatedAt, x.Post.State == FeedState.Hidden, x.Likes, x.LikedByMe, x.Comments);

    private static CommentResponse Response(PostComment x, string? name) => new(x.Id, x.PostId, x.AuthorId, x.AuthorRole, name,
        x.Content, x.CreatedAt, x.UpdatedAt, x.State == FeedState.Hidden);
}


