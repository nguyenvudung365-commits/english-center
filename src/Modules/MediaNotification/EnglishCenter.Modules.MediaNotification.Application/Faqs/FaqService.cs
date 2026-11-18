using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Domain.Faqs;

namespace EnglishCenter.Modules.MediaNotification.Application.Faqs;

public sealed record FaqRequest(string? Question, string? Answer, bool? IsPublished);
public sealed record FaqResponse(Guid Id, string Question, string Answer, bool IsPublished, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public interface IFaqStore
{
    Task AddAsync(Faq item, CancellationToken ct);
    Task<Faq?> FindAsync(Guid id, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Faq>> ListAsync(bool publishedOnly, bool? published, string? search, int page, int size, CancellationToken ct);
}

public sealed class FaqService(IFaqStore store, MediaAccess access, TimeProvider clock)
{
    // Công khai: chỉ FAQ đã xuất bản, không cần đăng nhập.
    public async Task<PagedResult<FaqResponse>> ListPublishedAsync(string? search, int page, int size, CancellationToken ct) =>
        await ListAsync(true, null, search, page, size, ct);

    public async Task<PagedResult<FaqResponse>> ListAllAsync(bool? published, string? search, int page, int size, CancellationToken ct)
    {
        await access.ActiveAsync(ct, Roles.Admin);
        return await ListAsync(false, published, search, page, size, ct);
    }

    public async Task<FaqResponse> CreateAsync(FaqRequest request, CancellationToken ct)
    {
        var actor = await access.ActiveAsync(ct, Roles.Admin);
        var item = Faq.Create(Question(request), Answer(request), request.IsPublished ?? false, actor.Id, clock.GetUtcNow().UtcDateTime);
        await store.AddAsync(item, ct);
        return Response(item);
    }

    public async Task<FaqResponse> UpdateAsync(Guid id, FaqRequest request, CancellationToken ct)
    {
        var actor = await access.ActiveAsync(ct, Roles.Admin);
        var (question, answer) = (Question(request), Answer(request));
        var item = await store.FindAsync(id, ct) ?? throw new MediaFailure(404, "FAQ not found.");
        item.Update(question, answer, request.IsPublished ?? false, actor.Id, clock.GetUtcNow().UtcDateTime);
        await store.SaveAsync(ct);
        return Response(item);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await access.ActiveAsync(ct, Roles.Admin);
        if (await store.FindAsync(id, ct) is null) throw new MediaFailure(404, "FAQ not found.");
        await store.DeleteAsync(id, ct);
    }

    private async Task<PagedResult<FaqResponse>> ListAsync(bool publishedOnly, bool? published, string? search, int page, int size, CancellationToken ct)
    {
        (page, size) = Paging.Validate(page, size);
        var result = await store.ListAsync(publishedOnly, published, Text.Search(search), page, size, ct);
        return new(result.Items.Select(Response).ToList(), result.Total, result.Page, result.PageSize);
    }

    private static string Question(FaqRequest r) => Text.Clean(r.Question, 300, "Question");
    private static string Answer(FaqRequest r) => Text.Clean(r.Answer, 4000, "Answer", multiline: true);
    private static FaqResponse Response(Faq x) => new(x.Id, x.Question, x.Answer, x.IsPublished, x.CreatedAt, x.UpdatedAt);
}
