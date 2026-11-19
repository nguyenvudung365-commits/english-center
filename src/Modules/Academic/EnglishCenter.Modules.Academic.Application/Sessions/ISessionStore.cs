using EnglishCenter.Modules.Academic.Domain.Sessions;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

public interface ISessionStore
{
    Task<Session?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Session>> RangeAsync(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<Guid> visibleClassIds, CancellationToken ct);
    Task CreateWithoutTeacherOverlapAsync(Session session, Guid teacherId, CancellationToken ct);
    Task<Session> ChangeAsync(Guid id, Guid classId, Guid teacherId, int expectedRevision, Session? replacement, CancellationToken ct);
}
