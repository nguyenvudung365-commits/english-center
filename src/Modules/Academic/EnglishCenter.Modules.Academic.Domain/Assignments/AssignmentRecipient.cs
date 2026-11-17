namespace EnglishCenter.Modules.Academic.Domain.Assignments;

public enum ObligationOutcome
{
    // Chưa đến hạn hoặc worker chưa chốt.
    Pending,
    // Có bài nộp trước hạn.
    Submitted,
    // Không nộp trước hạn và vẫn còn quyền lớp khi chốt → phát AssignmentMissed.
    Missed,
    // Bị thu hồi quyền lớp trước khi chốt → miễn nghĩa vụ (Q-07).
    Exempted
}

/// <summary>
/// Q-07: nghĩa vụ nộp bài theo roster active tại thời điểm công bố, lưu cùng transaction công bố.
/// Học viên vào lớp sau công bố không có dòng này nên không bị tính missed hồi tố.
/// </summary>
public sealed class AssignmentRecipient
{
    private AssignmentRecipient() { }
    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public ObligationOutcome Outcome { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    // Outbox tối giản cho AssignmentMissed: null = chưa phát thành công sau khi chốt Missed.
    public DateTimeOffset? MissedEventSentAt { get; private set; }

    public static AssignmentRecipient Create(Guid assignmentId, Guid studentId, DateTimeOffset assignedAt)
    {
        if (assignmentId == Guid.Empty || studentId == Guid.Empty) throw new ArgumentException("Invalid recipient.");
        return new() { AssignmentId = assignmentId, StudentId = studentId, AssignedAt = assignedAt.ToUniversalTime() };
    }

    public void Resolve(ObligationOutcome outcome, DateTimeOffset now)
    {
        if (outcome == ObligationOutcome.Pending) throw new ArgumentException("A final outcome is required.", nameof(outcome));
        // Trạng thái cuối không đổi lại: replay/worker chạy lại không làm sai lịch sử.
        if (Outcome != ObligationOutcome.Pending) return;
        Outcome = outcome; ResolvedAt = now.ToUniversalTime();
    }

    public void MarkMissedEventSent(DateTimeOffset now)
    {
        if (Outcome != ObligationOutcome.Missed) throw new InvalidOperationException("Only missed obligations publish events.");
        MissedEventSentAt ??= now.ToUniversalTime();
    }
}
