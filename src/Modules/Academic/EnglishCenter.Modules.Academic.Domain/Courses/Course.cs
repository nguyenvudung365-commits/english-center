namespace EnglishCenter.Modules.Academic.Domain.Courses;

public sealed class Course
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;

    private Course() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    public static Course Create(Guid id, string code, string name, string? description = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Course id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > CodeMaxLength)
            throw new ArgumentException("Course code is required and must not exceed 50 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
            throw new ArgumentException("Course name is required and must not exceed 200 characters.", nameof(name));
        return new Course { Id = id, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description };
    }
}
