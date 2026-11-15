using EnglishCenter.BuildingBlocks.EventBus;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Attendance;
using EnglishCenter.Modules.Academic.Application.Sessions;
using EnglishCenter.Modules.Academic.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Academic.Domain.Attendance;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Enrollment.Contracts;
using EnglishCenter.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using AttendanceEntity = EnglishCenter.Modules.Academic.Domain.Attendance.Attendance;

namespace EnglishCenter.Academic.Tests;

public sealed class AttendanceServiceTests
{
    private readonly FakeAcademicStore classes = new();
    private readonly FakeClassPolicy policy;
    private readonly AttendanceMemoryStore store = new();
    private readonly AttendanceDirectory directory = new();
    private readonly AttendanceBus bus = new();
    private readonly Class item;
    private readonly Guid student = Guid.NewGuid();
    private readonly DateTime now = new DateTime(2027, 1, 1, 10, 30, 0, DateTimeKind.Utc).AddTicks(1234567);

    public AttendanceServiceTests()
    {
        policy = new(classes);
        policy.As(Roles.Teacher);
        item = Class.Create("ATT", "Attendance", Guid.NewGuid(), ClassType.Main, new(2026, 1, 1), new(2028, 1, 1), 500, 0, policy.UserId!.Value, new(now));
        classes.Classes.Add(item);
        store.Session = Session.Create(item.Id, "Unit", "Lesson", SessionSkills.Listening, SessionType.Regular, null,
            now.Date.AddHours(10), now.Date.AddHours(11), now);
        store.Teacher = policy.UserId.Value;
        policy.Enrollments.Add((item.Id, student));
    }

    private AttendanceService Service => new(store, store, classes, policy.Access, policy, policy, directory, directory,
        bus, new FixedClock(new(now)), NullLogger<AttendanceService>.Instance);

    [Fact]
    public async Task CommitThenDispatchFailureKeepsSourceAndNoOpNeverPublishes()
    {
        bus.Failure = new InvalidOperationException("Synthetic dispatcher failure");
        var saved = await Service.RecordAsync(store.Session.Id, student, new("Present", null), default);
        Assert.Single(store.Sources);
        Assert.Equal(1, saved.Revision);
        var published = Assert.IsType<AttendanceRevisionRecordedIntegrationEvent>(Assert.Single(bus.Events));
        Assert.Equal(saved.Id, published.AttendanceId);
        Assert.Equal(saved.UpdatedAtUtc.UtcDateTime, published.RecordedAtUtc);
        Assert.True(bus.Strict);
        Assert.Equal(saved, await Service.RecordAsync(store.Session.Id, student, new("Present", 1), default));
        Assert.Single(bus.Events);
        Assert.Equal(409, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.RecordAsync(store.Session.Id, student, new("Present", null), default))).Status);
    }

    [Fact]
    public async Task LiveGuardIsCalledInsideAtomicStoreAndPreventsRevokedTarget()
    {
        store.BeforeRecheck = () => policy.Enrollments.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.RecordAsync(store.Session.Id, student, new("Present", null), default))).Status);
        Assert.Empty(store.Sources);
        Assert.Null(store.Row);
        Assert.Empty(bus.Events);
    }

    [Fact]
    public async Task ReplayOldRevisionUsesOriginalSourceAndPersistsFailedAttempt()
    {
        var saved = await Service.RecordAsync(store.Session.Id, student, new("Present", null), default);
        await Service.RecordAsync(store.Session.Id, student, new("Late", 1), default);
        policy.As(Roles.Staff);
        bus.Failure = new InvalidOperationException("Synthetic failure");
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.ReplayAsync(saved.Id, new(1, " recovery "), default));
        Assert.Equal(503, failure.Status);
        var attempt = Assert.Single(store.Attempts);
        Assert.Equal(ReplayDispatchStatus.Failed, attempt.DispatchStatus);
        Assert.Equal("recovery", attempt.Reason);
        Assert.NotNull(attempt.CompletedAtUtc);
        var replay = Assert.IsType<AttendanceRevisionRecordedIntegrationEvent>(bus.Events.Last());
        var original = Assert.IsType<AttendanceRevisionRecordedIntegrationEvent>(bus.Events.First());
        Assert.Equal(original.Status, replay.Status);
        Assert.Equal(original.Revision, replay.Revision);
        Assert.Equal(original.RecordedBy, replay.RecordedBy);
        Assert.Equal(original.RecordedAtUtc, replay.RecordedAtUtc);
        bus.Failure = null;
        Assert.Equal("Dispatched", (await Service.ReplayAsync(saved.Id, new(2, "retry"), default)).DispatchStatus);
        Assert.Equal(ReplayDispatchStatus.Dispatched, store.Attempts.Last().DispatchStatus);
    }

    [Fact]
    public async Task CancellationPropagatesAfterCommitAndReplayRemainsPending()
    {
        bus.Failure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Service.RecordAsync(store.Session.Id, student, new("Present", null), default));
        Assert.Single(store.Sources);
        policy.As(Roles.Admin);
        await Assert.ThrowsAsync<OperationCanceledException>(() => Service.ReplayAsync(store.Row!.Id, new(1, "cancelled"), default));
        Assert.Equal(ReplayDispatchStatus.Pending, Assert.Single(store.Attempts).DispatchStatus);
    }

    [Fact]
    public async Task RosterPagesAllNamedLiveCandidatesAndRetainsInactiveHistory()
    {
        await Service.RecordAsync(store.Session.Id, student, new("Present", null), default);
        policy.Enrollments.Clear();
        foreach (var id in Enumerable.Range(0, 205).Select(_ => Guid.NewGuid()))
        {
            directory.Ids.Add(id);
            policy.Enrollments.Add((item.Id, id));
        }
        var all = new List<RosterItem>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await Service.RosterAsync(store.Session.Id, new(page, 100), default);
            Assert.Equal(206, result.Total);
            all.AddRange(result.Items);
        }
        Assert.Equal(206, all.Select(x => x.StudentId).Distinct().Count());
        Assert.All(all.Where(x => x.IsActive), x => { Assert.NotNull(x.FullName); Assert.NotNull(x.StudentCode); Assert.Null(x.Attendance); });
        Assert.False(Assert.Single(all, x => x.StudentId == student).IsActive);
        Assert.Empty((await Service.RosterAsync(store.Session.Id, new(int.MaxValue, 100), default)).Items);
        Assert.Single(bus.Events);
    }

    private sealed class AttendanceDirectory : IEnrollmentRoster, IStudentDisplayDirectory
    {
        internal List<Guid> Ids { get; } = [];
        public Task<IReadOnlyList<Guid>> GetActiveStudentIdsAsync(Guid classId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(Ids);
        public Task<IReadOnlyList<StudentDisplay>> FindActiveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StudentDisplay>>(ids.Where(Ids.Contains).Select(id => new StudentDisplay(id, "Student", "HV-test")).ToArray());
    }

    private sealed class AttendanceBus : IEventBus
    {
        internal List<IIntegrationEvent> Events { get; } = [];
        internal Exception? Failure;
        internal bool Strict;
        public Task PublishAsync<TEvent>(TEvent value, CancellationToken cancellationToken = default, bool throwOnHandlerFailure = false) where TEvent : IIntegrationEvent
        {
            Events.Add(value); Strict = throwOnHandlerFailure;
            if (Failure is not null) return Task.FromException(Failure);
            return Task.CompletedTask;
        }
    }

    private sealed class AttendanceMemoryStore : IAttendanceStore, ISessionStore
    {
        internal Session Session = null!;
        internal Guid Teacher;
        internal Action? BeforeRecheck;
        internal AttendanceEntity? Row;
        internal List<AttendanceRevision> Sources { get; } = [];
        internal List<AttendanceReplayAttempt> Attempts { get; } = [];
        public Task<AttendanceEntity?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Row?.Id == id ? Row : null);
        Task<Session?> ISessionStore.FindAsync(Guid id, CancellationToken ct) => Task.FromResult<Session?>(id == Session.Id ? Session : null);
        public Task<IReadOnlyList<AttendanceEntity>> ForSessionAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AttendanceEntity>>(Row is null ? [] : [Row]);
        public async Task<AttendanceWriteResult> WriteAsync(Guid sessionId, Guid studentId, AttendanceStatus status, int? expectedRevision, Guid actor,
            DateTime rawNow, Func<Session, Guid, CancellationToken, Task> recheck, CancellationToken ct)
        {
            BeforeRecheck?.Invoke();
            await recheck(Session, Teacher, ct);
            if (Row?.Revision != expectedRevision) throw new AcademicFailure(409, "Stale revision");
            AttendanceRevision? source;
            if (Row is null)
            {
                Row = AttendanceEntity.Create(sessionId, Session.ClassId, studentId, status, actor, rawNow);
                source = AttendanceRevision.Capture(Row, null);
            }
            else source = Row.Change(status, actor, rawNow);
            if (source is not null) Sources.Add(source);
            return new(Row, source);
        }
        public Task<AttendanceRevision?> SourceAsync(Guid attendanceId, int revision, CancellationToken ct) => Task.FromResult(Sources.SingleOrDefault(x => x.AttendanceId == attendanceId && x.Revision == revision));
        public Task AddReplayAsync(AttendanceReplayAttempt attempt, CancellationToken ct) { Attempts.Add(attempt); return Task.CompletedTask; }
        public Task CompleteReplayAsync(AttendanceReplayAttempt attempt, CancellationToken ct) => Task.CompletedTask;
        public Task<Page<AttendanceEntity>> ListAsync(AttendanceFilter filter, PageQuery page, CancellationToken ct) => throw new NotSupportedException();
        public Task<Summary> SummaryAsync(AttendanceFilter filter, CancellationToken ct) => throw new NotSupportedException();
        public Task<Page<AttendanceRevision>> HistoryAsync(Guid attendanceId, PageQuery page, CancellationToken ct) => throw new NotSupportedException();
        public Task<Page<AttendanceReplayAttempt>> ReplaysAsync(Guid attendanceId, PageQuery page, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Session>> RangeAsync(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<Guid> visibleClassIds, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateWithoutTeacherOverlapAsync(Session session, Guid teacherId, CancellationToken ct) => throw new NotSupportedException();
    }
}
