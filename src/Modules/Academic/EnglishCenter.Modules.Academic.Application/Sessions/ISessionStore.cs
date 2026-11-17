using EnglishCenter.Modules.Academic.Domain.Sessions;

namespace EnglishCenter.Modules.Academic.Application.Sessions;

public interface ISessionStore
{
    Task<Session?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Session>> RangeAsync(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<Guid> visibleClassIds, CancellationToken ct);
    // Every future edit/import/reassignment must take the same teacher lock; direct SQL writers are unsupported.
    Task CreateWithoutTeacherOverlapAsync(Session session, Guid teacherId, CancellationToken ct);
}
