namespace EnglishCenter.Modules.Academic.Domain.Teachers;

// Read model: bản sao giáo viên từ Identity, chỉ được ghi bởi handler UserRegistered của Academic.
public sealed class Teacher
{
    public const int FullNameMaxLength = 200;
    public const int EmailMaxLength = 254;

    private Teacher() { }

    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public long ProfileVersion { get; private set; }

    public static Teacher Create(Guid userId, string fullName, string email, long profileVersion = 0)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Teacher id is required.", nameof(userId));
        var teacher = new Teacher { Id = userId };
        teacher.UpdateProfile(fullName, email);
        teacher.ProfileVersion = profileVersion;
        return teacher;
    }

    public bool ApplySnapshot(string fullName, string email, long profileVersion)
    {
        if (profileVersion <= ProfileVersion) return false;
        UpdateProfile(fullName, email);
        ProfileVersion = profileVersion;
        return true;
    }

    public void UpdateProfile(string fullName, string email)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > FullNameMaxLength)
            throw new ArgumentException("Teacher name is required and must not exceed 200 characters.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > EmailMaxLength)
            throw new ArgumentException("Teacher email is required and must not exceed 254 characters.", nameof(email));
        FullName = fullName.Trim();
        Email = email.Trim();
    }
}
