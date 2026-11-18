using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Domain.Feedback;

namespace EnglishCenter.Modules.MediaNotification.Application.Feedback;

public sealed record FeedbackRequest(string? Title, string? Content, int? Rating);
public sealed record FeedbackStatusRequest(string? Status, Guid? ExpectedRevision);
public sealed record FeedbackResponse(Guid Id, Guid StudentId, string Title, string Content, int Rating, string Status,
    DateTime CreatedAtUtc, Guid? HandledBy, DateTime? HandledAtUtc, Guid Revision);

public interface IFeedbackStore
{
    Task AddAsync(StudentFeedback item, CancellationToken ct);
    Task<StudentFeedback?> FindAsync(Guid id, CancellationToken ct);
    // false khi bản ghi đã bị người khác đổi (revision lệch) giữa lúc đọc và lúc lưu.
    Task<bool> TrySaveAsync(CancellationToken ct);
    Task<PagedResult<StudentFeedback>> ListAsync(Guid? student, string? status, string? search, int page, int size, CancellationToken ct);
}

public sealed class FeedbackService(IFeedbackStore store, MediaAccess access, TimeProvider clock)
{
    public async Task<FeedbackResponse> CreateAsync(FeedbackRequest request, CancellationToken ct)
    {
        var actor = await access.ActiveAsync(ct, Roles.Student);
        var title = Text.Clean(request.Title, 150, "Title");
        var content = Text.Clean(request.Content, 2000, "Content", multiline: true);
        if (request.Rating is not (>= 1 and <= 5)) throw new MediaFailure(400, "Rating must be between 1 and 5.");
        var item = StudentFeedback.Create(actor.Id, title, content, request.Rating.Value, clock.GetUtcNow().UtcDateTime);
        await store.AddAsync(item, ct);
        return Response(item, actor);
    }

    public async Task<PagedResult<FeedbackResponse>> ListAsync(string? status, string? search, int page, int size, CancellationToken ct)
    {
        var actor = await ReaderAsync(ct);
        if (status is not null && !FeedbackStatus.IsValid(status)) throw new MediaFailure(400, "Unknown status.");
        (page, size) = Paging.Validate(page, size);
        var result = await store.ListAsync(actor.IsManager ? null : actor.Id, status, Text.Search(search), page, size, ct);
        return new(result.Items.Select(x => Response(x, actor)).ToList(), result.Total, result.Page, result.PageSize);
    }

    public async Task<FeedbackResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var actor = await ReaderAsync(ct);
        var item = await store.FindAsync(id, ct);
        if (item is null || (!actor.IsManager && item.StudentId != actor.Id)) throw new MediaFailure(404, "Feedback not found.");
        return Response(item, actor);
    }

    public async Task<FeedbackResponse> UpdateStatusAsync(Guid id, FeedbackStatusRequest request, CancellationToken ct)
    {
        var actor = await access.ActiveAsync(ct, Roles.Staff, Roles.Admin);
        if (!FeedbackStatus.IsValid(request.Status)) throw new MediaFailure(400, "Unknown status.");
        if (request.ExpectedRevision is not { } expected || expected == Guid.Empty)
            throw new MediaFailure(400, "expectedRevision is required.");
        var item = await store.FindAsync(id, ct) ?? throw new MediaFailure(404, "Feedback not found.");
        if (item.Revision != expected) throw new MediaFailure(409, "Feedback changed; reload and retry.");
        if (!FeedbackStatus.CanMove(item.Status, request.Status!))
            throw new MediaFailure(409, "Invalid status transition (New → InProgress → Resolved; never reopened).");
        item.MoveTo(request.Status!, actor.Id, clock.GetUtcNow().UtcDateTime);
        if (!await store.TrySaveAsync(ct)) throw new MediaFailure(409, "Feedback changed; reload and retry.");
        return Response(item, actor);
    }

    private Task<Actor> ReaderAsync(CancellationToken ct) => access.ActiveAsync(ct, Roles.Student, Roles.Staff, Roles.Admin);

    // Student không thấy danh tính người xử lý.
    private static FeedbackResponse Response(StudentFeedback x, Actor actor) => new(x.Id, x.StudentId, x.Title, x.Content,
        x.Rating, x.Status, x.CreatedAt, actor.IsManager ? x.HandledBy : null, x.HandledAt, x.Revision);
}

