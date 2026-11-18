using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Academic.Contracts;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.MediaNotification.Domain.Notifications;

namespace EnglishCenter.Modules.MediaNotification.Application.Notifications;

// Tiêu đề chung, không nhúng điểm/URL/nội dung file. Khóa chống trùng lấy từ khóa nghiệp vụ của event nguồn,
// nên phát lại (replay) cùng event không tạo thông báo mới.
public sealed class NotificationHandlers(INotificationStore store, IAssignmentDirectory assignments, TimeProvider clock) :
    IIntegrationEventHandler<StudentEnrolledIntegrationEvent>,
    IIntegrationEventHandler<InvoicePaidIntegrationEvent>,
    IIntegrationEventHandler<WarningIssuedIntegrationEvent>,
    IIntegrationEventHandler<AssignmentPublishedIntegrationEvent>,
    IIntegrationEventHandler<SubmissionGradedIntegrationEvent>
{
    public Task HandleAsync(StudentEnrolledIntegrationEvent e, CancellationToken ct = default) => store.AddOnceAsync(
        [Make(e.StudentId, "StudentEnrolled", e.EnrollmentId, 0, "Bạn đã được ghi danh vào một lớp học", "Class", e.ClassId)], ct);

    public Task HandleAsync(InvoicePaidIntegrationEvent e, CancellationToken ct = default) => store.AddOnceAsync(
        [Make(e.StudentId, "InvoicePaid", e.InvoiceId, 0, "Hóa đơn của bạn đã được xác nhận thanh toán", "Invoice", e.InvoiceId)], ct);

    public Task HandleAsync(WarningIssuedIntegrationEvent e, CancellationToken ct = default) => store.AddOnceAsync(
        [Make(e.StudentId, "WarningIssued", e.WarningId, 0, "Bạn có một cảnh báo học tập mới", "Warning", e.WarningId)], ct);

    public Task HandleAsync(SubmissionGradedIntegrationEvent e, CancellationToken ct = default) => store.AddOnceAsync(
        [Make(e.StudentId, "SubmissionGraded", e.SubmissionId, e.GradeVersion, "Bài nộp của bạn đã được chấm điểm", "Assignment", e.AssignmentId)], ct);

    // Người nhận lấy từ snapshot lúc công bố (Academic contract); bài legacy không có snapshot thì không có thông báo.
    public async Task HandleAsync(AssignmentPublishedIntegrationEvent e, CancellationToken ct = default)
    {
        var recipients = await assignments.RecipientsAsync(e.AssignmentId, ct);
        await store.AddOnceAsync(recipients.Select(x => Make(x, "AssignmentPublished", e.AssignmentId, 0,
            "Có bài tập mới trong lớp của bạn", "Assignment", e.AssignmentId)).ToList(), ct);
    }

    private Notification Make(Guid recipient, string kind, Guid source, int version, string title, string targetType, Guid target) =>
        Notification.Create(recipient, kind, source, version, title, targetType, target, clock.GetUtcNow().UtcDateTime);
}
