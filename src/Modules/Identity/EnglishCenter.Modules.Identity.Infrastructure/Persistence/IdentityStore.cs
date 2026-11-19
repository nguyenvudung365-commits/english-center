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
    public Task<bool> IsActiveInRoleAsync(Guid id, string? expectedRole, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.Id == id && !x.IsLocked && x.Role == expectedRole, ct);
    public async Task<IReadOnlyDictionary<Guid, string>> FullNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    public Task<RefreshSession?> FindSessionAsync(string hash, CancellationToken ct) =>
        db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    public async Task<IReadOnlyList<RefreshSession>> SessionsAsync(Guid userId, CancellationToken ct) =>
        await db.RefreshSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);
    public async Task<IReadOnlyList<User>> UsersAsync(string? role, CancellationToken ct)
    {
        // Sort client-side: SQLite cannot ORDER BY DateTimeOffset.
        var users = await db.Users.AsNoTracking().Where(x => role == null || x.Role == role).ToListAsync(ct);
        return users.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
    }
    public async Task<ManagedAccountPage> ListManagedAccountsAsync(string role, string query, string? mobileDigits, int page, int pageSize, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking().Where(x => x.Role == role);
        if (query.Length != 0)
        {
            var term = query.ToUpperInvariant();
            users = users.Where(x => x.FullName.ToUpper().Contains(term) || x.NormalizedEmail.Contains(term) ||
                (x.StudentCode != null && x.StudentCode.Contains(term)) || (x.Phone != null &&
                    (x.Phone.Contains(term) || (mobileDigits != null && x.Phone.Replace(" ", "").Replace(".", "").Replace("-", "").EndsWith(mobileDigits)))));
        }

        var total = await users.CountAsync(ct);
        var items = await users.OrderBy(x => x.FullName).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ManagedAccount(x.Id, x.FullName, x.Email, x.Phone, x.StudentCode, x.IsLocked, x.Revision))
            .ToListAsync(ct);
        return new ManagedAccountPage(items, total, page, pageSize);
    }
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
