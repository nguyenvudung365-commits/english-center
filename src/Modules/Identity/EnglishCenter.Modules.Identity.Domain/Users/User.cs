using EnglishCenter.BuildingBlocks.Security;

namespace EnglishCenter.Modules.Identity.Domain.Users;

public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public Guid? AvatarFileId { get; private set; }
    public string? StudentCode { get; private set; }
    public long ProfileVersion { get; private set; }
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
    public void AssignStudentCode(string code)
    {
        if (Role != Roles.Student || StudentCode is not null)
            throw new InvalidOperationException("Student code can only be assigned once to a Student.");
        if (code.Length != 11 || !code.StartsWith("HV", StringComparison.Ordinal) || code[8] != '-' ||
            code.AsSpan(2, 6).ContainsAnyExceptInRange('0', '9') || code.AsSpan(9, 2).ContainsAnyExceptInRange('0', '9') ||
            code.Substring(2, 6) == "000000" || code[9..] != CreatedAt.UtcDateTime.ToString("yy", System.Globalization.CultureInfo.InvariantCulture))
            throw new ArgumentException("Invalid student code.", nameof(code));
        StudentCode = code;
        Touch();
    }

    public void UpdateProfile(string fullName, string email, string? phone, Guid? avatarFileId)
    {
        fullName = fullName.Trim();
        email = email.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (fullName.Length is 0 or > 200 || email.Length is 0 or > 254 || phone?.Length > 32 || avatarFileId == Guid.Empty)
            throw new ArgumentException("Invalid profile fields.");
        if (FullName != fullName || Email != email) ProfileVersion = checked(ProfileVersion + 1);
        FullName = fullName;
        Email = email;
        NormalizedEmail = NormalizeEmail(email);
        Phone = phone;
        AvatarFileId = avatarFileId;
        Touch();
    }

    public void Touch() => Revision = Guid.NewGuid();
    public void ChangePassword(string hash) { PasswordHash = hash; Touch(); }
    public void SetLocked(bool locked) { IsLocked = locked; Touch(); }
}
