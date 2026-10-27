using EnglishCenter.Modules.Identity.Application.Authentication;
using EnglishCenter.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EnglishCenter.Modules.Identity.Infrastructure.Persistence;

internal sealed class IdentityStore(IdentityDbContext db) : IIdentityStore
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email, ct);
    public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<RefreshSession?> FindSessionAsync(string hash, CancellationToken ct) =>
        db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    public async Task<IReadOnlyList<RefreshSession>> SessionsAsync(Guid userId, CancellationToken ct) =>
        await db.RefreshSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
    public void AddUser(User user) => db.Users.Add(user);
    public void AddSession(RefreshSession session) => db.RefreshSessions.Add(session);
    public async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new IdentityFailure(409, "Account changed concurrently. Retry with current credentials.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new IdentityFailure(409, "Account data already exists.");
        }
    }
}
