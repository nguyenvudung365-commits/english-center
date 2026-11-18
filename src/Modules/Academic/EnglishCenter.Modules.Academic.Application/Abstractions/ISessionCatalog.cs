namespace EnglishCenter.Modules.Academic.Application.Abstractions;

public sealed record SessionInfo(Guid Id, Guid ClassId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Unit, string? Lesson);

/// <summary>
/// Điểm nối giữa bài tập (TV2) và buổi học (TV1, US-08). Bài mới bắt buộc gán buổi cùng lớp trước khi công bố (AC-05);
/// Unit/Lesson bỏ trống thì lấy mặc định từ buổi. Bản triển khai đọc bảng buổi học của TV1 (<c>Sessions.SessionCatalog</c>).
/// </summary>
public interface ISessionCatalog
{
    Task<SessionInfo?> FindAsync(Guid sessionId, CancellationToken ct);
}
