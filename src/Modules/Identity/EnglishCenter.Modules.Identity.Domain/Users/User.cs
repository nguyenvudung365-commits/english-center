using EnglishCenter.BuildingBlocks.Security;

namespace EnglishCenter.Modules.Identity.Domain.Users;

public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsLocked { get; private set; }
    public Guid Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(string email, string fullName, string role, string passwordHash, DateTimeOffset now)
    {
        if (role is not (Roles.Student or Roles.Teacher or Roles.Staff or Roles.Admin))
            throw new ArgumentException("Unknown role.", nameof(role));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        return new User
        {
            Id = Guid.NewGuid(), Email = email.Trim(), NormalizedEmail = NormalizeEmail(email),
            FullName = fullName.Trim(), Role = role, PasswordHash = passwordHash,
            CreatedAt = now, Revision = Guid.NewGuid()
        };
    }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    public void Touch() => Revision = Guid.NewGuid();
    public void ChangePassword(string hash) { PasswordHash = hash; Touch(); }
    public void SetLocked(bool locked) { IsLocked = locked; Touch(); }
}
