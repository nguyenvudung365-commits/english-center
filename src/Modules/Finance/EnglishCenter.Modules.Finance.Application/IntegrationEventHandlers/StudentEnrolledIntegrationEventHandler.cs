using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Domain.Invoices;

namespace EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers;

public sealed class StudentEnrolledIntegrationEventHandler(IInvoiceStore store, IEventBus events, TimeProvider clock)
    : IIntegrationEventHandler<StudentEnrolledIntegrationEvent>
{
    public async Task HandleAsync(StudentEnrolledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var e = integrationEvent;
        var candidate = Invoice.Create(e.EnrollmentId, e.StudentId, e.ClassId, e.TuitionFee,
            e.EnrolledAtUtc, clock.GetUtcNow().UtcDateTime);
        var invoice = await store.CreateOnceAsync(candidate, cancellationToken);
        if (invoice.Id == candidate.Id)
            await events.PublishAsync(new InvoiceCreatedIntegrationEvent(invoice.Id, invoice.EnrollmentId,
                invoice.StudentId, invoice.Amount, invoice.DueDate), cancellationToken);
    }
}
