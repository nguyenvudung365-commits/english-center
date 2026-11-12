namespace EnglishCenter.Modules.Enrollment.Domain.Enrollments;

// Membership authority for coursework. Billing and capacity workflows are separate capabilities.
public sealed class ClassEnrollment
{
    private ClassEnrollment() { }
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public static ClassEnrollment Create(Guid classId, Guid studentId, DateTimeOffset now)
    {
        if (classId == Guid.Empty || studentId == Guid.Empty) throw new ArgumentException("Class and student are required.");
        return new() { Id = Guid.NewGuid(), ClassId = classId, StudentId = studentId, IsActive = true, CreatedAt = now };
    }
    public void SetActive(bool active) => IsActive = active;
}
