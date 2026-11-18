using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Application.Feed;
using EnglishCenter.Modules.MediaNotification.Domain.Feed;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class ClassFeedStore(MediaNotificationDbContext db) : IClassFeedStore
{
    public async Task AddAsync(ClassPost post, CancellationToken ct)
    {
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddAsync(PostComment comment, CancellationToken ct)
    {
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
    }

    public Task<ClassPost?> FindPostAsync(Guid id, CancellationToken ct) => db.Posts.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<PostComment?> FindCommentAsync(Guid id, CancellationToken ct) => db.Comments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<PagedResult<PostView>> PostsAsync(Guid classId, bool includeHidden, Guid viewer, int page, int size, CancellationToken ct)
    {
        var states = States(includeHidden);
        var query = db.Posts.AsNoTracking().Where(x => x.ClassId == classId && states.Contains(x.State));
        var total = await query.CountAsync(ct);
        var posts = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = posts.Select(x => x.Id).ToList();
        var likes = await db.Likes.AsNoTracking().Where(x => ids.Contains(x.PostId)).GroupBy(x => x.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.PostId, x => x.Count, ct);
        var mine = (await db.Likes.AsNoTracking().Where(x => x.UserId == viewer && ids.Contains(x.PostId))
            .Select(x => x.PostId).ToListAsync(ct)).ToHashSet();
        var comments = await db.Comments.AsNoTracking().Where(x => ids.Contains(x.PostId) && x.State == FeedState.Visible)
            .GroupBy(x => x.PostId).Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PostId, x => x.Count, ct);
        return new(posts.Select(x => new PostView(x, likes.GetValueOrDefault(x.Id), mine.Contains(x.Id),
            comments.GetValueOrDefault(x.Id))).ToList(), total, page, size);
    }

    public async Task<PagedResult<PostComment>> CommentsAsync(Guid postId, bool includeHidden, int page, int size, CancellationToken ct)
    {
        var states = States(includeHidden);
        var query = db.Comments.AsNoTracking().Where(x => x.PostId == postId && states.Contains(x.State));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, total, page, size);
    }

    public async Task<int> SetLikeAsync(Guid postId, Guid user, bool liked, DateTime now, CancellationToken ct)
    {
        if (!liked) await db.Likes.Where(x => x.PostId == postId && x.UserId == user).ExecuteDeleteAsync(ct);
        else if (!await db.Likes.AnyAsync(x => x.PostId == postId && x.UserId == user, ct))
        {
            db.Likes.Add(PostLike.Create(postId, user, now));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Khóa (PostId, UserId) đã có do like song song: coi như đã like; lỗi khác vẫn nổi lên.
                db.ChangeTracker.Clear();
                if (!await db.Likes.AnyAsync(x => x.PostId == postId && x.UserId == user, ct)) throw;
            }
        }
        return await db.Likes.CountAsync(x => x.PostId == postId, ct);
    }

    private static string[] States(bool includeHidden) => includeHidden ? [FeedState.Visible, FeedState.Hidden] : [FeedState.Visible];
}
