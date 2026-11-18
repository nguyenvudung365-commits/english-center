using EnglishCenter.Modules.Academic.Application.Abstractions;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

// Bài tập (TV2) đọc buổi học thật của TV1 trong cùng module Academic. Không kiểm tra quyền ở đây:
// AssignmentService đã kiểm tra người gọi quản lý lớp và buổi phải thuộc đúng lớp đó.
internal sealed class SessionCatalog(ISessionStore store) : ISessionCatalog
{
    public async Task<SessionInfo?> FindAsync(Guid sessionId, CancellationToken ct)
    {
        var item = await store.FindAsync(sessionId, ct);
        return item is null ? null
            : new(item.Id, item.ClassId, Utc(item.StartsAtUtc), Utc(item.EndsAtUtc), item.Unit, item.Lesson);
    }

    // Cột lưu UTC; một số provider (SQLite) trả Kind = Unspecified nên gắn lại offset 0 cho chắc.
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
