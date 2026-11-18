using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Domain.Notifications;

namespace EnglishCenter.Modules.MediaNotification.Application.Notifications;

public sealed record NotificationResponse(Guid Id, string Kind, string Title, string TargetType, Guid TargetId,
    DateTime CreatedAtUtc, DateTime? ReadAtUtc, bool IsRead);
public sealed record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int Total, int Page, int PageSize, int UnreadCount);
public sealed record UnreadCountResponse(int UnreadCount);
public sealed record MarkedReadResponse(int Marked);
public sealed record NotificationSlice(IReadOnlyList<Notification> Items, int Total, int Unread);

public interface INotificationStore
{
    // Bỏ qua bản ghi đã có cùng khóa (RecipientId, Kind, SourceId, SourceVersion): replay không tạo trùng.
    Task AddOnceAsync(IReadOnlyList<Notification> items, CancellationToken ct);
    Task<NotificationSlice> ListAsync(Guid recipient, bool unreadOnly, int page, int size, CancellationToken ct);
    Task<int> UnreadCountAsync(Guid recipient, CancellationToken ct);
    // false khi thông báo không tồn tại hoặc không thuộc người nhận.
    Task<bool> MarkReadAsync(Guid id, Guid recipient, DateTime now, CancellationToken ct);
    Task<int> MarkAllReadAsync(Guid recipient, DateTime now, CancellationToken ct);
}

public sealed class NotificationService(INotificationStore store, MediaAccess access, TimeProvider clock)
{
    public async Task<NotificationListResponse> ListAsync(bool unreadOnly, int page, int size, CancellationToken ct)
    {
        var actor = await access.ActiveAsync(ct, MediaAccess.Everyone);
        (page, size) = Paging.Validate(page, size);
        var slice = await store.ListAsync(actor.Id, unreadOnly, page, size, ct);
        return new(slice.Items.Select(Response).ToList(), slice.Total, page, size, slice.Unread);
    }

    public async Task<UnreadCountResponse> UnreadCountAsync(CancellationToken ct) =>
        new(await store.UnreadCountAsync((await access.ActiveAsync(ct, MediaAccess.Everyone)).Id, ct));

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var actor = await access.ActiveAsync(ct, MediaAccess.Everyone);
        if (!await store.MarkReadAsync(id, actor.Id, clock.GetUtcNow().UtcDateTime, ct))
            throw new MediaFailure(404, "Notification not found.");
    }

    public async Task<MarkedReadResponse> MarkAllReadAsync(CancellationToken ct) =>
        new(await store.MarkAllReadAsync((await access.ActiveAsync(ct, MediaAccess.Everyone)).Id, clock.GetUtcNow().UtcDateTime, ct));

    private static NotificationResponse Response(Notification x) => new(x.Id, x.Kind, x.Title, x.TargetType, x.TargetId,
        x.CreatedAt, x.ReadAt, x.ReadAt is not null);
}
