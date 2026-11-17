namespace EnglishCenter.Modules.Academic.Application.Abstractions;

public sealed record SessionInfo(Guid Id, Guid ClassId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Unit, string? Lesson);

/// <summary>
/// Điểm nối giữa bài tập (TV2) và buổi học (TV1, US-08). Khi TV1 đăng ký bản triển khai thật
/// (IsAvailable = true) thì bài mới bắt buộc gán buổi cùng lớp trước khi công bố (AC-05).
/// Trước đó dùng <see cref="UnavailableSessionCatalog"/>: không nhận SessionId và cho công bố không cần buổi.
/// </summary>
public interface ISessionCatalog
{
    bool IsAvailable { get; }
    Task<SessionInfo?> FindAsync(Guid sessionId, CancellationToken ct);
}

public sealed class UnavailableSessionCatalog : ISessionCatalog
{
    public bool IsAvailable => false;
    public Task<SessionInfo?> FindAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionInfo?>(null);
}
