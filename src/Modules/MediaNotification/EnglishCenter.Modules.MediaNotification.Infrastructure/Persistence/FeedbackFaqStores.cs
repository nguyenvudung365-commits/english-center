using EnglishCenter.Modules.MediaNotification.Application.Common;
using EnglishCenter.Modules.MediaNotification.Application.Faqs;
using EnglishCenter.Modules.MediaNotification.Application.Feedback;
using EnglishCenter.Modules.MediaNotification.Domain.Faqs;
using EnglishCenter.Modules.MediaNotification.Domain.Feedback;
using Microsoft.EntityFrameworkCore;

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence;

internal sealed class FeedbackStore(MediaNotificationDbContext db) : IFeedbackStore
{
    public async Task AddAsync(StudentFeedback item, CancellationToken ct)
    {
        db.Feedbacks.Add(item);
        await db.SaveChangesAsync(ct);
    }

    public Task<StudentFeedback?> FindAsync(Guid id, CancellationToken ct) => db.Feedbacks.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<int> CountSinceAsync(Guid student, DateTime since, CancellationToken ct) =>
        db.Feedbacks.CountAsync(x => x.StudentId == student && x.CreatedAt > since, ct);

    public async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    public async Task<PagedResult<StudentFeedback>> ListAsync(Guid? student, string? status, string? search, int page, int size, CancellationToken ct)
    {
        var query = db.Feedbacks.AsNoTracking();
        if (student.HasValue) query = query.Where(x => x.StudentId == student);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (search is not null)
        {
            var text = search.ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(text) || x.Content.ToLower().Contains(text));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, total, page, size);
    }
}

internal sealed class FaqStore(MediaNotificationDbContext db) : IFaqStore
{
    public async Task AddAsync(Faq item, CancellationToken ct)
    {
        db.Faqs.Add(item);
        await db.SaveChangesAsync(ct);
    }

    public Task<Faq?> FindAsync(Guid id, CancellationToken ct) => db.Faqs.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public Task DeleteAsync(Guid id, CancellationToken ct) => db.Faqs.Where(x => x.Id == id).ExecuteDeleteAsync(ct);

    public async Task<PagedResult<Faq>> ListAsync(bool publishedOnly, bool? published, string? search, int page, int size, CancellationToken ct)
    {
        var query = db.Faqs.AsNoTracking();
        if (publishedOnly) query = query.Where(x => x.IsPublished);
        else if (published.HasValue) query = query.Where(x => x.IsPublished == published);
        if (search is not null)
        {
            var text = search.ToLower();
            query = query.Where(x => x.Question.ToLower().Contains(text) || x.Answer.ToLower().Contains(text));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items, total, page, size);
    }
}
