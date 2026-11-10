namespace EnglishCenter.Modules.Finance.Domain.Invoices;

public sealed class Invoice
{
    public Guid Id { get; private set; }
    public string InvoiceNo { get; private set; } = "";
    public Guid EnrollmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid ClassId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; } = "Unpaid";
    public DateTime IssuedAt { get; private set; }
    public DateOnly DueDate { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public Guid? ConfirmedBy { get; private set; }
    private Invoice() { }
    public static Invoice Create(Guid enrollment, Guid student, Guid classId, decimal amount, DateTime enrolledAt, DateTime issuedAt)
    {
        if (enrollment == Guid.Empty || student == Guid.Empty || classId == Guid.Empty || amount < 0 ||
            amount > 999_999_999_999m || decimal.Truncate(amount) != amount || enrolledAt.Kind != DateTimeKind.Utc ||
            issuedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid invoice event.");
        var id = Guid.NewGuid();
        return new Invoice { Id = id, InvoiceNo = id.ToString("N"), EnrollmentId = enrollment, StudentId = student,
            ClassId = classId, Amount = amount, IssuedAt = issuedAt,
            DueDate = DateOnly.FromDateTime(enrolledAt.AddHours(7)).AddDays(7) };
    }
}
