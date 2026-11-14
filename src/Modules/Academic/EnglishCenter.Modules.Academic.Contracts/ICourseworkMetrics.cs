namespace EnglishCenter.Modules.Academic.Contracts;

/// <summary>
/// Số liệu bài tập của một học viên cho các bài có hạn nộp trong [FromUtc, ToUtc).
/// Assigned/Missed chỉ tính bài có nghĩa vụ (snapshot lúc công bố); AverageScore = null khi chưa có bài được chấm.
/// </summary>
public sealed record StudentCourseworkMetrics(Guid StudentId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Assigned,
    int Submitted, int Graded, int Missed, decimal? AverageScore);

// Hợp đồng số liệu TV2 cung cấp cho thống kê US-29 (TV1 tổng hợp); không join schema khác.
public interface ICourseworkMetrics
{
    Task<StudentCourseworkMetrics> ForStudentAsync(Guid studentId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}
