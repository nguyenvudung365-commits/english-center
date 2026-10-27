using System.Collections.Concurrent;
using EnglishCenter.Modules.Finance.Application.Abstractions;

namespace EnglishCenter.Modules.Finance.Infrastructure.Persistence;

// TODO: thay bằng bảng finance.invoices ở sprint sau.
internal sealed class InMemoryInvoiceCreationRecorder : IInvoiceCreationRecorder
{
    private readonly ConcurrentDictionary<Guid, (Guid StudentId, decimal Amount)> _records = new();

    public IReadOnlyCollection<Guid> RecordedEnrollmentIds => _records.Keys.ToArray();

    public Task RecordAsync(Guid enrollmentId, Guid studentId, decimal amount, CancellationToken ct)
    {
        // Idempotent theo EnrollmentId: nhận trùng event không tạo bản ghi thứ hai.
        _records.TryAdd(enrollmentId, (studentId, amount));
        return Task.CompletedTask;
    }
}
