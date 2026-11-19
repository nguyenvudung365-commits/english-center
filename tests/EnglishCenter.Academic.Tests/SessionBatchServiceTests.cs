using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Sessions;
using EnglishCenter.Modules.Academic.Domain.Classes;
using EnglishCenter.Modules.Academic.Domain.Sessions;
using EnglishCenter.Modules.Academic.Domain.Teachers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static EnglishCenter.Academic.Tests.SessionDomainTests;

namespace EnglishCenter.Academic.Tests;

public sealed class SessionBatchServiceTests
{
    private readonly FakeAcademicStore classes = new();
    private readonly FakeBatchStore store = new();
    private readonly FakeClassPolicy policy;
    private readonly Class item;
    private readonly Guid teacher = Guid.NewGuid();

    public SessionBatchServiceTests()
    {
        policy = new(classes);
        policy.Accounts[teacher] = Roles.Teacher;
        item = NewClass(teacher);
        classes.Classes.Add(item);
        classes.Teachers.Add(Teacher.Create(teacher, "Teacher", "teacher@example.test"));
    }

    private SessionBatchService Service
    {
        get
        {
            var single = new SessionService(new UnusedSessionStore(), classes, policy.Access, policy,
                new FixedClock(new(Start.AddDays(-1))), new RecordingEventBus(), NullLogger<SessionService>.Instance);
            return new(single, store, classes, policy.Access);
        }
    }

    private static BatchSessionSlot Slot(int day, int hour = 10) =>
        new(new DateTimeOffset(2027, 1, day, hour, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, day, hour + 1, 0, 0, TimeSpan.Zero));

    private CreateSessionBatchRequest Request(params BatchSessionSlot?[] slots) => new(item.Id, " Unit 1 ", " Bài học ",
        ["speaking", "LISTENING"], "regular", " ", slots!);

    [Fact]
    public async Task Preview_ReportsEachSlot_AndNeverWrites()
    {
        store.Persisted.Add(0); // index into the windows the service sends: only slot 1 is still conflict-free
        var preview = await Service.PreviewAsync(Request(Slot(1), Slot(2), Slot(1, 10) with { StartsAtUtc = new DateTimeOffset(2027, 1, 1, 10, 30, 0, TimeSpan.Zero),
            EndsAtUtc = new DateTimeOffset(2027, 1, 1, 11, 30, 0, TimeSpan.Zero) }), default);
        Assert.False(preview.CanCreate);
        Assert.Equal(["BatchOverlap", "TeacherOverlap", "BatchOverlap"], preview.Items.Select(x => x.Status));
        Assert.Equal([0, 1, 2], preview.Items.Select(x => x.Index));
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(1, store.OverlapCalls);
    }

    [Fact]
    public async Task Preview_AllClear_CanCreate_AndTouchingSlotsDoNotConflict()
    {
        var preview = await Service.PreviewAsync(Request(Slot(1, 10), Slot(1, 11), Slot(2)), default);
        Assert.True(preview.CanCreate);
        Assert.All(preview.Items, x => Assert.Equal("Ok", x.Status));
    }

    [Fact]
    public async Task Preview_InvalidSlot_IsReportedPerSlot_AndSkipsPersistedCheckForIt()
    {
        var outside = new BatchSessionSlot(new DateTimeOffset(2027, 2, 1, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 2, 1, 11, 0, 0, TimeSpan.Zero));
        var preview = await Service.PreviewAsync(Request(Slot(1), outside, new(null, null)), default);
        Assert.False(preview.CanCreate);
        Assert.Equal(["Ok", "Invalid", "Invalid"], preview.Items.Select(x => x.Status));
        Assert.NotEmpty(preview.Items[1].Message!);
        Assert.Single(store.LastWindows);
    }

    [Fact]
    public async Task Create_WritesAllSessionsOnceThroughAtomicStore_WithAssignedTeacher()
    {
        using var cancellation = new CancellationTokenSource();
        var created = await Service.CreateAsync(Request(Slot(2), Slot(1)), cancellation.Token);
        Assert.Equal(1, store.CreateCalls);
        Assert.Equal(teacher, store.TeacherId);
        Assert.Equal(cancellation.Token, store.Token);
        Assert.Equal(2, store.Created.Count);
        Assert.Equal(2, created.Count);
        Assert.All(created, x => { Assert.Equal("Unit 1", x.Unit); Assert.Equal("Bài học", x.Lesson); Assert.Equal("Teacher", x.TeacherName); });
        Assert.Equal(new[] { "Listening", "Speaking" }, created[0].Skills);
    }

    [Fact]
    public async Task Create_IntraBatchOverlap_Is409_WithoutStoreWrite()
    {
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(Request(Slot(1), Slot(1)), default));
        Assert.Equal(409, failure.Status);
        Assert.Contains("Slots[0]", failure.Errors!.Keys);
        Assert.Contains("Slots[1]", failure.Errors.Keys);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task Create_InvalidSlot_Is400WithSlotKey_WithoutStoreWrite()
    {
        var outside = new BatchSessionSlot(new DateTimeOffset(2027, 2, 1, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 2, 1, 11, 0, 0, TimeSpan.Zero));
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(Request(Slot(1), outside), default));
        Assert.Equal(400, failure.Status);
        Assert.Contains("Slots[1].EndsAtUtc", failure.Errors!.Keys);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task Create_SharedFieldError_IsReportedOnce_Unprefixed()
    {
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(Request(Slot(1), Slot(2)) with { Unit = " " }, default));
        Assert.Equal(400, failure.Status);
        Assert.Equal(["Unit"], failure.Errors!.Keys);
        Assert.Single(failure.Errors["Unit"]);
    }

    [Fact]
    public async Task Create_PropagatesStoreConflictUnchanged_WithoutRetry()
    {
        store.Failure = new AcademicFailure(409, "Teacher already has an overlapping session.");
        var actual = await Record.ExceptionAsync(() => Service.CreateAsync(Request(Slot(1)), default));
        Assert.Same(store.Failure, actual);
        Assert.Equal(1, store.CreateCalls);
        Assert.Empty(store.Created);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SessionBatchService.MaxSlots + 1)]
    public async Task SlotCount_OutsideLimits_Is400BeforeAnyLookup(int count)
    {
        var slots = Enumerable.Range(0, count).Select(_ => Slot(1)).ToArray();
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.PreviewAsync(Request(slots), default));
        Assert.Equal(400, failure.Status);
        Assert.Contains("Slots", failure.Errors!.Keys);
        Assert.Equal(0, store.OverlapCalls);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Student)]
    public async Task OnlyManagers_CanPreviewOrCreate(string role)
    {
        policy.As(role);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.PreviewAsync(Request(Slot(1)), default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(Request(Slot(1)), default))).Status);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(0, store.OverlapCalls);
    }

    [Fact]
    public async Task NullSlotEntry_IsInvalidSlot_NotACrash()
    {
        var preview = await Service.PreviewAsync(Request(Slot(1), null), default);
        Assert.Equal(["Ok", "Invalid"], preview.Items.Select(x => x.Status));
    }

    private sealed class FakeBatchStore : ISessionBatchStore
    {
        internal List<int> Persisted { get; } = [];
        internal List<Session> Created { get; } = [];
        internal Exception? Failure { get; set; }
        internal int CreateCalls, OverlapCalls;
        internal Guid TeacherId;
        internal CancellationToken Token;
        internal IReadOnlyList<(DateTime Start, DateTime End)> LastWindows = [];

        public Task<IReadOnlyList<int>> TeacherOverlapIndexesAsync(Guid teacherId, IReadOnlyList<(DateTime Start, DateTime End)> windows, CancellationToken ct)
        {
            OverlapCalls++; LastWindows = windows;
            return Task.FromResult<IReadOnlyList<int>>(Persisted.Where(x => x < windows.Count).ToArray());
        }

        public Task CreateManyWithoutTeacherOverlapAsync(IReadOnlyList<Session> sessions, Guid teacherId, CancellationToken ct)
        {
            CreateCalls++; TeacherId = teacherId; Token = ct;
            if (Failure is not null) return Task.FromException(Failure);
            Created.AddRange(sessions); return Task.CompletedTask;
        }
    }

    private sealed class UnusedSessionStore : ISessionStore
    {
        public Task<Session?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Session>> RangeAsync(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<Guid> visibleClassIds, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateWithoutTeacherOverlapAsync(Session session, Guid teacherId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Session> ChangeAsync(Guid id, Guid classId, Guid teacherId, int expectedRevision, Session? replacement, CancellationToken ct) => throw new NotSupportedException();
    }
}
