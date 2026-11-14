namespace EnglishCenter.Modules.Academic.Contracts;

public sealed record AssignmentSummary(Guid Id, Guid ClassId, string Title, string Kind, DateTimeOffset OpensAtUtc,
    DateTimeOffset ClosesAtUtc, DateTimeOffset? PublishedAt);

/// <summary>
/// Truy vấn tối thiểu cho module khác (vd. inbox TV4) dựng lại thông báo từ dữ liệu bền vững thay vì đọc bảng Academic.
/// Người gọi vẫn phải kiểm tra quyền hiện tại khi người dùng mở link.
/// </summary>
public interface IAssignmentDirectory
{
    /// <summary>Chỉ trả bài đã công bố hoặc legacy; bài nháp trả null.</summary>
    Task<AssignmentSummary?> FindAsync(Guid assignmentId, CancellationToken ct);

    /// <summary>Snapshot học viên có nghĩa vụ tại thời điểm công bố (Q-07). Bài legacy không có snapshot → rỗng.</summary>
    Task<IReadOnlyList<Guid>> RecipientsAsync(Guid assignmentId, CancellationToken ct);
}
