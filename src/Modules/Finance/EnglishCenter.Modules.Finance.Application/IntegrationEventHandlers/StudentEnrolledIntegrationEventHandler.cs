using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers;

public sealed class StudentEnrolledIntegrationEventHandler
    : IIntegrationEventHandler<StudentEnrolledIntegrationEvent>
{
    private readonly IInvoiceCreationRecorder _recorder;
    private readonly ILogger<StudentEnrolledIntegrationEventHandler> _logger;

    public StudentEnrolledIntegrationEventHandler(
        IInvoiceCreationRecorder recorder,
        ILogger<StudentEnrolledIntegrationEventHandler> logger)
    {
        _recorder = recorder;
        _logger = logger;
    }

    public async Task HandleAsync(StudentEnrolledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Nhận StudentEnrolled: EnrollmentId={EnrollmentId}, StudentId={StudentId}, Học phí={Amount}",
            integrationEvent.EnrollmentId,
            integrationEvent.StudentId,
            integrationEvent.TuitionFee);

        await _recorder.RecordAsync(
            integrationEvent.EnrollmentId,
            integrationEvent.StudentId,
            integrationEvent.TuitionFee,
            cancellationToken);
    }
}
