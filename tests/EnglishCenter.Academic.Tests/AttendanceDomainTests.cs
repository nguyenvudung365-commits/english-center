using EnglishCenter.Modules.Academic.Application.Attendance;
using EnglishCenter.Modules.Academic.Domain.Attendance;
using Xunit;
using AttendanceEntity = EnglishCenter.Modules.Academic.Domain.Attendance.Attendance;

namespace EnglishCenter.Academic.Tests;

public sealed class AttendanceDomainTests
{
    private static readonly DateTime Now = new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

    [Fact]
    public void CorrectionPreservesFirstMetadataAndImmutableSourceAndNoOp()
    {
        var firstActor = Guid.NewGuid();
        var row = AttendanceEntity.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AttendanceStatus.Present, firstActor, Now);
        var first = AttendanceRevision.Capture(row, null);
        var secondActor = Guid.NewGuid();
        var second = row.Change(AttendanceStatus.Late, secondActor, Now.AddHours(1))!;
        Assert.Equal(2, row.Revision);
        Assert.Equal(firstActor, row.RecordedBy);
        Assert.Equal(first.ChangedAtUtc, row.RecordedAtUtc);
        Assert.Equal(secondActor, row.UpdatedBy);
        Assert.Equal(AttendanceStatus.Present, first.Status);
        Assert.Null(first.PreviousStatus);
        Assert.Equal(1, first.Revision);
        Assert.Equal(AttendanceStatus.Present, second.PreviousStatus);
        Assert.Equal(AttendanceStatus.Late, second.Status);
        Assert.Equal(row.Id, second.AttendanceId);
        Assert.Equal(row.SessionId, second.SessionId);
        Assert.Equal(row.ClassId, second.ClassId);
        Assert.Equal(row.StudentId, second.StudentId);
        Assert.Equal(0, row.UpdatedAtUtc.Ticks % 10);
        Assert.Equal(DateTimeKind.Utc, row.UpdatedAtUtc.Kind);
        Assert.Null(row.Change(AttendanceStatus.Late, firstActor, Now.AddHours(2)));
        Assert.Equal(2, row.Revision);
        Assert.Equal(secondActor, row.UpdatedBy);
        Assert.Equal(second.ChangedAtUtc, row.UpdatedAtUtc);
    }

    [Fact]
    public void InvalidStatusIdentifiersAndNonUtcTimeCannotCreateSource()
    {
        Assert.Throws<ArgumentException>(() => AttendanceEntity.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), AttendanceStatus.Present, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => AttendanceEntity.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (AttendanceStatus)7, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => AttendanceEntity.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AttendanceStatus.Present, Guid.NewGuid(), DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    [Theory]
    [InlineData(true, ReplayDispatchStatus.Dispatched)]
    [InlineData(false, ReplayDispatchStatus.Failed)]
    public void ReplayAuditIsPendingThenSingleCompletion(bool dispatched, ReplayDispatchStatus expected)
    {
        var attempt = AttendanceReplayAttempt.Create(Guid.NewGuid(), 2, Guid.NewGuid(), Now, "  recovery  ");
        Assert.Equal("recovery", attempt.Reason);
        Assert.Equal(ReplayDispatchStatus.Pending, attempt.DispatchStatus);
        Assert.Null(attempt.CompletedAtUtc);
        Assert.Equal(0, attempt.RequestedAtUtc.Ticks % 10);
        attempt.Complete(dispatched, Now.AddTicks(99));
        Assert.Equal(expected, attempt.DispatchStatus);
        Assert.Equal(0, attempt.CompletedAtUtc!.Value.Ticks % 10);
        Assert.Throws<InvalidOperationException>(() => attempt.Complete(dispatched, Now));
    }

    [Fact]
    public void SummaryCountsOnlyActualLatestStatusesAndRoundsAwayFromZero()
    {
        var summary = Summary.FromStatuses([AttendanceStatus.Present, AttendanceStatus.AbsentExcused, AttendanceStatus.LeftEarly]);
        Assert.Equal(3, summary.RecordedCount);
        Assert.Equal(33, summary.Percentages.Present);
        Assert.Equal(1, summary.AbsentCount);
        Assert.Equal(0, summary.LateCount);
        Assert.Equal(1, summary.LeftEarlyCount);
        Assert.Equal(13, Summary.FromCounts(new(1, 7, 0, 0, 0, 0)).Percentages.Present);
        var empty = Summary.FromStatuses([]);
        Assert.Equal(0, empty.RecordedCount);
        Assert.Equal(0, empty.Percentages.AbsentUnexcused);
        Assert.Equal(0, empty.AbsentCount);
    }
}
