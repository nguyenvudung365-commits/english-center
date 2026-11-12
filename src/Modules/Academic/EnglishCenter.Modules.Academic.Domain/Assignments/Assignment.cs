namespace EnglishCenter.Modules.Academic.Domain.Assignments;

public sealed class Assignment
{
    private Assignment() { }
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset OpensAtUtc { get; private set; }
    public DateTimeOffset ClosesAtUtc { get; private set; }
    public static Assignment Create(Guid classId, string title, string description, DateTimeOffset opens, DateTimeOffset closes)
    {
        if (classId == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200 ||
            description.Length > 5000 || opens >= closes) throw new ArgumentException("Invalid assignment.");
        return new() { Id = Guid.NewGuid(), ClassId = classId, Title = title.Trim(), Description = description.Trim(),
            OpensAtUtc = opens.ToUniversalTime(), ClosesAtUtc = closes.ToUniversalTime() };
    }
}
