using EnglishCenter.Modules.MediaNotification.Application.Notifications;
using EnglishCenter.Modules.MediaNotification.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class NotificationStore(MediaNotificationDbContext db) : INotificationStore
{
    public async Task AddOnceAsync(IReadOnlyList<Notification> items, CancellationToken ct)
    {
        var fresh = new List<Notification>();
        foreach (var item in items)
            if (!await ExistsAsync(item, ct)) fresh.Add(item);
        if (fresh.Count == 0) return;
        db.Notifications.AddRange(fresh);
        try
        {
            await db.SaveChangesAsync(ct);
            return;
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }

        // Một replay song song đã ghi trước: chỉ thêm phần còn thiếu, lỗi khác vẫn nổi lên.
        foreach (var item in fresh)
        {
            if (await ExistsAsync(item, ct)) continue;
            db.Notifications.Add(item);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    public async Task<NotificationSlice> ListAsync(Guid recipient, bool unreadOnly, int page, int size, CancellationToken ct)
    {
        var all = db.Notifications.AsNoTracking().Where(x => x.RecipientId == recipient);
        var unread = await all.CountAsync(x => x.ReadAt == null, ct);
        var query = unreadOnly ? all.Where(x => x.ReadAt == null) : all;
        var total = unreadOnly ? unread : await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, total, unread);
    }

    public Task<int> UnreadCountAsync(Guid recipient, CancellationToken ct) =>
        db.Notifications.CountAsync(x => x.RecipientId == recipient && x.ReadAt == null, ct);

    public async Task<bool> MarkReadAsync(Guid id, Guid recipient, DateTime now, CancellationToken ct)
    {
        var changed = await db.Notifications.Where(x => x.Id == id && x.RecipientId == recipient && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, (DateTime?)now), ct);
        return changed > 0 || await db.Notifications.AnyAsync(x => x.Id == id && x.RecipientId == recipient, ct);
    }

    public Task<int> MarkAllReadAsync(Guid recipient, DateTime now, CancellationToken ct) =>
        db.Notifications.Where(x => x.RecipientId == recipient && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, (DateTime?)now), ct);

    private Task<bool> ExistsAsync(Notification x, CancellationToken ct) => db.Notifications.AnyAsync(y =>
        y.RecipientId == x.RecipientId && y.Kind == x.Kind && y.SourceId == x.SourceId && y.SourceVersion == x.SourceVersion, ct);
}
