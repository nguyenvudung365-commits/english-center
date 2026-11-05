using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.Modules.Enrollment.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Finance.Application.Abstractions;
using EnglishCenter.Modules.Finance.Application.IntegrationEventHandlers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public class Tv3Week1Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Tv3Week1Tests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData(0)]
    [InlineData(1_500_000)]
    public async Task FinanceHandler_ForwardsEnrollmentStudentAmountAndCancellation(int tuitionFee)
    {
        var recorder = new CapturingRecorder();
        var handler = new StudentEnrolledIntegrationEventHandler(
            recorder, NullLogger<StudentEnrolledIntegrationEventHandler>.Instance);
        var enrolled = CreateEvent(Guid.NewGuid(), tuitionFee);
        using var cancellation = new CancellationTokenSource();

        await handler.HandleAsync(enrolled, cancellation.Token);

        Assert.Equal(enrolled.EnrollmentId, recorder.EnrollmentId);
        Assert.Equal(enrolled.StudentId, recorder.StudentId);
        Assert.Equal(enrolled.TuitionFee, recorder.Amount);
        Assert.Equal(cancellation.Token, recorder.Token);
        Assert.Equal(1, recorder.Calls);
    }

    [Fact]
    public async Task SameEnrollment_WithDifferentEventIds_IsRecordedOnce()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var recorder = _factory.Services.GetRequiredService<IInvoiceCreationRecorder>();
        var first = CreateEvent(Guid.NewGuid(), 1_500_000m);
        var replay = new StudentEnrolledIntegrationEvent(
            first.EnrollmentId, first.StudentId, first.ClassId, first.TuitionFee, first.EnrolledAtUtc);
        Assert.NotEqual(first.EventId, replay.EventId);

        await bus.PublishAsync(first);
        await bus.PublishAsync(replay);

        Assert.Single(recorder.RecordedEnrollmentIds, id => id == first.EnrollmentId);
    }

    [Fact]
    public async Task ConcurrentPublishes_ForSameEnrollment_AreRecordedOnce()
    {
        var bus = _factory.Services.GetRequiredService<IEventBus>();
        var recorder = _factory.Services.GetRequiredService<IInvoiceCreationRecorder>();
        var first = CreateEvent(Guid.NewGuid(), 1_500_000m);
        // Release all workers together so the recorder is used from multiple threads.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishes = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            await bus.PublishAsync(new StudentEnrolledIntegrationEvent(
                first.EnrollmentId, first.StudentId, first.ClassId, first.TuitionFee, first.EnrolledAtUtc));
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(publishes);

        Assert.Single(recorder.RecordedEnrollmentIds, id => id == first.EnrollmentId);
    }

    private static StudentEnrolledIntegrationEvent CreateEvent(Guid enrollmentId, decimal amount) =>
        new(enrollmentId, Guid.NewGuid(), Guid.NewGuid(), amount, DateTime.UtcNow);

    private sealed class CapturingRecorder : IInvoiceCreationRecorder
    {
        public Guid EnrollmentId { get; private set; }
        public Guid StudentId { get; private set; }
        public decimal Amount { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public IReadOnlyCollection<Guid> RecordedEnrollmentIds =>
            Calls == 0 ? Array.Empty<Guid>() : new[] { EnrollmentId };

        public Task RecordAsync(Guid enrollmentId, Guid studentId, decimal amount, CancellationToken ct)
        {
            EnrollmentId = enrollmentId;
            StudentId = studentId;
            Amount = amount;
            Token = ct;
            Calls++;
            return Task.CompletedTask;
        }
    }
}
