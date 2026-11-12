namespace EnglishCenter.Modules.Academic.Domain.Classes;

public sealed class ClassStudent
{
    public Guid EnrollmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid ClassId { get; private set; }
    private ClassStudent() { }
    public ClassStudent(Guid enrollmentId, Guid studentId, Guid classId)
    {
        if (enrollmentId == Guid.Empty || studentId == Guid.Empty || classId == Guid.Empty)
            throw new ArgumentException("Invalid class membership.");
        EnrollmentId = enrollmentId; StudentId = studentId; ClassId = classId;
    }
}
