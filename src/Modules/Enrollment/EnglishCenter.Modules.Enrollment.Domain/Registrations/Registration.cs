namespace EnglishCenter.Modules.Enrollment.Domain.Registrations;

public sealed class Student
{
    public Guid StudentId { get; private set; }
    public string FullName { get; private set; } = "";
    public string Email { get; private set; } = "";
    private Student() { }
    public Student(Guid id, string name, string email)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 255) throw new ArgumentException("Invalid student snapshot.");
        StudentId = id; FullName = name; Email = email;
    }
}

public sealed class ClassSnapshot
{
    public Guid ClassId { get; private set; }
    public string ClassCode { get; private set; } = "";
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public int MaxStudents { get; private set; }
    public int CurrentStudents { get; private set; }
    public decimal TuitionFee { get; private set; }
    private ClassSnapshot() { }
    public ClassSnapshot(Guid id, string code, DateOnly start, DateOnly end, int capacity, decimal tuition)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(code) || code.Length > 50 || end <= start || capacity <= 0 ||
            !Registration.ValidAmount(tuition)) throw new ArgumentException("Invalid class snapshot.");
        ClassId = id; ClassCode = code; StartDate = start; EndDate = end; MaxStudents = capacity; TuitionFee = tuition;
    }
}

public sealed class Registration
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid ClassId { get; private set; }
    public string Status { get; private set; } = "Active";
    // Coursework access can be revoked without cancelling the paid seat or its invoice.
    public bool IsActive { get; private set; } = true;
    public DateTime EnrolledAt { get; private set; }
    // Lưu học phí tại lúc ghi danh để đối soát/phát lại event.
    public decimal TuitionFee { get; private set; }
    public int MaxAbsences { get; private set; }
    public int MaxLates { get; private set; }
    public int MaxSkippedAssignments { get; private set; }
    public int AbsenceCount { get; private set; }
    public int LateCount { get; private set; }
    public int SkippedAssignmentCount { get; private set; }
    private Registration() { }
    public Registration(Guid student, Guid classId, decimal tuition, DateTime at, int absences, int lates, int skipped)
    {
        if (student == Guid.Empty || classId == Guid.Empty || !ValidAmount(tuition) || at.Kind != DateTimeKind.Utc ||
            absences < 0 || lates < 0 || skipped < 0) throw new ArgumentException("Invalid enrollment.");
        Id = Guid.NewGuid(); StudentId = student; ClassId = classId; TuitionFee = tuition; EnrolledAt = at;
        MaxAbsences = absences; MaxLates = lates; MaxSkippedAssignments = skipped;
    }
    public static bool ValidAmount(decimal value) => value >= 0 && value <= 999_999_999_999m && decimal.Truncate(value) == value;
}
