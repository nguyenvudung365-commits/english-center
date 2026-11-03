using System.ComponentModel.DataAnnotations;
using EnglishCenter.Modules.Identity.Domain.Users;

namespace EnglishCenter.Modules.Identity.Application.Authentication;

public sealed record LoginRequest([Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password);
public sealed record RefreshRequest([Required, MaxLength(256)] string RefreshToken);
public sealed record ChangePasswordRequest([Required, MaxLength(128)] string CurrentPassword,
    [Required, MinLength(8), MaxLength(128)] string NewPassword);
public sealed record CreateUserRequest([Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(200)] string FullName,
    [Required, MinLength(8), MaxLength(128)] string Password, [Required] string Role);
public sealed record UserResponse(Guid Id, string Email, string FullName, string Role);
public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed class IdentityFailure(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public interface IIdentityStore
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<User?> FindUserAsync(Guid id, CancellationToken ct);
    Task<RefreshSession?> FindSessionAsync(string hash, CancellationToken ct);
    Task<IReadOnlyList<RefreshSession>> SessionsAsync(Guid userId, CancellationToken ct);
    void AddUser(User user);
    void AddSession(RefreshSession session);
    Task SaveAsync(CancellationToken ct);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string? hash, string password);
}

public interface ITokenService
{
    TokenResponse Issue(User user, DateTimeOffset now);
    string HashRefreshToken(string token);
}
