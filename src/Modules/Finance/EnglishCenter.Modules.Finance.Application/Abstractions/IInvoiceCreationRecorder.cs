namespace EnglishCenter.Modules.Finance.Application.Abstractions;

/// <summary>
/// Khung tạm cho luồng StudentEnrolled -> hóa đơn. Sprint sau thay bằng bảng finance.invoices.
/// </summary>
public interface IInvoiceCreationRecorder
{
    Task RecordAsync(Guid enrollmentId, Guid studentId, decimal amount, CancellationToken ct);
    IReadOnlyCollection<Guid> RecordedEnrollmentIds { get; }
}
