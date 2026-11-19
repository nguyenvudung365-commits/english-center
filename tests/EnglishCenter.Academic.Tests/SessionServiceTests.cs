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

public sealed class SessionServiceTests
{
    private readonly FakeAcademicStore classes = new();
    private readonly RecordingSessionStore sessions = new();
    private readonly FakeClassPolicy policy;
    private readonly Class item;
    private readonly Guid teacher = Guid.NewGuid();
    public SessionServiceTests()
    {
        policy = new(classes);
        policy.Accounts[teacher] = Roles.Teacher;
        item = NewClass(teacher);
        classes.Classes.Add(item);
        classes.Teachers.Add(Teacher.Create(teacher, "Teacher", "teacher@example.test"));
    }
    private SessionService Service => new(sessions, classes, policy.Access, policy,
        new FixedClock(new(Start.AddDays(-1).AddTicks(1234567))), new RecordingEventBus(), NullLogger<SessionService>.Instance);
    private CreateSessionRequest Request => new(item.Id, new DateTimeOffset(2027, 1, 1, 17, 0, 0, TimeSpan.FromHours(7)),
        new DateTimeOffset(2027, 1, 1, 18, 0, 0, TimeSpan.FromHours(7)), " Unit ' 🗓 ", " Bài học ", ["speaking", "LISTENING"], "regular", " ");

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Create_NormalizesAndUsesAtomicStoreOnce_WithAssignedTeacherAndCancellation(string role)
    {
        policy.As(role);
        using var cancellation = new CancellationTokenSource();
        var result = await Service.CreateAsync(Request, cancellation.Token);
        var saved = Assert.Single(sessions.Items);
        Assert.Equal(1, sessions.CreateCalls);
        Assert.Equal(teacher, sessions.TeacherId);
        Assert.Equal(cancellation.Token, sessions.Token);
        Assert.Equal(Start, saved.StartsAtUtc);
        Assert.Equal(Start.AddHours(1), saved.EndsAtUtc);
        Assert.Equal(Start.AddDays(-1).AddTicks(1234560), saved.CreatedAtUtc);
        Assert.Equal("Unit ' 🗓", result.Unit);
        Assert.Equal("Bài học", result.Lesson);
        Assert.Equal(new[] { "Listening", "Speaking" }, result.Skills);
        Assert.Equal("Regular", result.SessionType);
        Assert.Null(result.ExamSlot);
        Assert.Equal(item.Id, result.ClassId);
        Assert.Equal("Teacher", result.TeacherName);
        Assert.Equal(TimeSpan.Zero, result.EndsAtUtc.Offset);
        Assert.Contains((teacher, (string?)Roles.Teacher), policy.AccountChecks);
    }

    [Theory]
    [InlineData("missing-class", "ClassId")]
    [InlineData("empty-class", "ClassId")]
    [InlineData("missing-start", "StartsAtUtc")]
    [InlineData("missing-end", "EndsAtUtc")]
    [InlineData("end-before-start", "EndsAtUtc")]
    [InlineData("null-unit", "Unit")]
    [InlineData("long-lesson", "Lesson")]
    [InlineData("null-skills", "Skills")]
    [InlineData("empty-skills", "Skills")]
    [InlineData("numeric-skills", "Skills")]
    [InlineData("duplicate-skills", "Skills")]
    [InlineData("numeric-type", "SessionType")]
    [InlineData("regular-slot", "ExamSlot")]
    [InlineData("submicro-start", "StartsAtUtc")]
    [InlineData("submicro-end", "EndsAtUtc")]
    [InlineData("collapsed", "StartsAtUtc")]
    public async Task Create_InvalidRequest_HasFieldErrorsWithoutStoreCalls(string scenario, string field)
    {
        var request = scenario switch
        {
            "missing-class" => Request with { ClassId = null }, "empty-class" => Request with { ClassId = Guid.Empty },
            "missing-start" => Request with { StartsAtUtc = null }, "missing-end" => Request with { EndsAtUtc = null },
            "end-before-start" => Request with { EndsAtUtc = Request.StartsAtUtc }, "null-unit" => Request with { Unit = null },
            "long-lesson" => Request with { Lesson = new string('l', 201) }, "null-skills" => Request with { Skills = null },
            "empty-skills" => Request with { Skills = [] }, "numeric-skills" => Request with { Skills = ["1"] },
            "duplicate-skills" => Request with { Skills = ["Listening", "listening"] },
            "numeric-type" => Request with { SessionType = "0" },
            "submicro-start" => Request with { StartsAtUtc = Request.StartsAtUtc!.Value.AddTicks(1) },
            "submicro-end" => Request with { EndsAtUtc = Request.EndsAtUtc!.Value.AddTicks(1) },
            "collapsed" => Request with { StartsAtUtc = new DateTimeOffset(Start.AddTicks(1)), EndsAtUtc = new DateTimeOffset(Start.AddTicks(2)) },
            _ => Request with { ExamSlot = "Exam" }
        };
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(request, default));
        Assert.Equal(400, failure.Status); Assert.Contains(field, failure.Errors!.Keys);
        Assert.Equal(0, sessions.CreateCalls); Assert.Empty(sessions.Items);
    }

    [Theory]
    [InlineData("teacher", 403)]
    [InlineData("revoked-manager", 403)]
    [InlineData("missing-class", 404)]
    [InlineData("inactive-teacher", 400)]
    [InlineData("outside-window", 400)]
    public async Task Create_RejectsPolicyAndWindowFailuresBeforeAtomicWrite(string scenario, int status)
    {
        if (scenario == "teacher") policy.As(Roles.Teacher, teacher);
        if (scenario == "revoked-manager") policy.Accounts.Remove(policy.UserId!.Value);
        if (scenario == "missing-class") classes.Classes.Clear();
        if (scenario == "inactive-teacher") policy.Accounts.Remove(teacher);
        var request = scenario == "outside-window" ? Request with { StartsAtUtc = new DateTimeOffset(Start.AddDays(-10)) } : Request;
        Assert.Equal(status, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(request, default))).Status);
        Assert.Equal(0, sessions.CreateCalls); Assert.Empty(sessions.Items);
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("provider")]
    [InlineData("cancelled")]
    public async Task Create_PropagatesAtomicFailure_UnchangedWithoutRetry(string kind)
    {
        sessions.Failure = kind switch { "overlap" => new AcademicFailure(409, "Overlap"),
            "provider" => new InvalidOperationException("Synthetic provider failure"), _ => new OperationCanceledException() };
        var actual = await Record.ExceptionAsync(() => Service.CreateAsync(Request, default));
        Assert.Same(sessions.Failure, actual);
        Assert.Equal(1, sessions.CreateCalls); Assert.Empty(sessions.Items);
    }

    [Fact]
    public async Task Reads_UseLiveScope_NormalizeBoundsAndSort_WithoutLeakingOtherClasses()
    {
        var other = NewClass(Guid.NewGuid(), "B"); classes.Classes.Add(other);
        policy.As(Roles.Student); policy.Enrollments.Add((item.Id, policy.UserId!.Value));
        var later = NewSession(item.Id, Start.AddHours(1)); var first = NewSession(item.Id, Start);
        var tied = NewSession(item.Id, Start); var hidden = NewSession(other.Id, Start);
        sessions.Items.AddRange([later, tied, hidden, first]);
        var result = await Service.RangeAsync(new(Request.StartsAtUtc, Request.EndsAtUtc!.Value.AddDays(1)), default);
        Assert.Equal(new[] { first, tied, later }.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Select(x => x.Id), result.Select(x => x.Id));
        Assert.Equal(new[] { item.Id }, sessions.VisibleIds);
        Assert.Equal(Start, sessions.From); Assert.Equal(Start.AddHours(1).AddDays(1), sessions.To);
        Assert.Equal(1, sessions.RangeCalls);
        Assert.Equal(first.Id, (await Service.GetAsync(first.Id, default)).Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.GetAsync(hidden.Id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.GetAsync(Guid.NewGuid(), default))).Status);
        policy.Enrollments.Clear();
        Assert.Empty(await Service.RangeAsync(new(Request.StartsAtUtc, Request.EndsAtUtc), default));
        Assert.Empty(sessions.VisibleIds);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.RangeAsync(new(Request.StartsAtUtc, Request.EndsAtUtc, item.Id), default))).Status);
        policy.Accounts.Clear(); var finds = sessions.FindCalls;
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.GetAsync(first.Id, default))).Status);
        Assert.Equal(finds, sessions.FindCalls);
    }

    [Theory]
    [InlineData("from", "FromUtc")]
    [InlineData("to", "ToUtc")]
    [InlineData("reverse", "ToUtc")]
    [InlineData("too-long", "ToUtc")]
    [InlineData("class", "ClassId")]
    [InlineData("submicro-from", "FromUtc")]
    [InlineData("submicro-to", "ToUtc")]
    public async Task Range_RejectsInvalidQueryWithoutStoreCall(string scenario, string field)
    {
        var from = new DateTimeOffset(Start);
        var query = scenario switch { "from" => new SessionRangeQuery(null, from), "to" => new(from, null),
            "reverse" => new(from, from), "too-long" => new(from, from.AddDays(93).AddTicks(1)),
            "submicro-from" => new(from.AddTicks(1), from.AddDays(1)), "submicro-to" => new(from, from.AddDays(1).AddTicks(1)),
            _ => new(from, from.AddDays(1), Guid.Empty) };
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service.RangeAsync(query, default));
        Assert.Equal(400, failure.Status); Assert.Contains(field, failure.Errors!.Keys); Assert.Equal(0, sessions.RangeCalls);
    }

    [Fact]
    public async Task Range_Accepts93ElapsedDays_AndExplicitClassScope()
    {
        Assert.Empty(await Service.RangeAsync(new(new(Start), new(Start.AddDays(93)), item.Id), default));
        Assert.Equal(new[] { item.Id }, sessions.VisibleIds); Assert.Equal(1, sessions.RangeCalls);
    }

    [Fact]
    public async Task PrecisionValidation_PrecedesClassLookupAndScope_ButAuthenticationRemainsFirst()
    {
        var invalid = Request with { ClassId = Guid.NewGuid(), StartsAtUtc = Request.StartsAtUtc!.Value.AddTicks(1) };
        var createError = await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(invalid, default));
        Assert.Equal(400, createError.Status); Assert.Contains("StartsAtUtc", createError.Errors!.Keys);
        policy.As(Roles.Student);
        var range = new SessionRangeQuery(Request.StartsAtUtc!.Value.AddTicks(1), Request.EndsAtUtc, Guid.NewGuid());
        var rangeError = await Assert.ThrowsAsync<AcademicFailure>(() => Service.RangeAsync(range, default));
        Assert.Equal(400, rangeError.Status); Assert.Contains("FromUtc", rangeError.Errors!.Keys);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.CreateAsync(invalid, default))).Status);
        policy.IsAuthenticated = false;
        Assert.Equal(401, (await Assert.ThrowsAsync<AcademicFailure>(() => Service.RangeAsync(range, default))).Status);
        Assert.Equal(0, sessions.RangeCalls); Assert.Equal(0, sessions.CreateCalls);
    }

    private static Session NewSession(Guid classId, DateTime start) => Session.Create(classId, "Unit", "Lesson",
        SessionSkills.Listening, SessionType.Regular, null, start, start.AddMinutes(30), Start.AddDays(-1));

    private sealed class RecordingSessionStore : ISessionStore
    {
        internal List<Session> Items { get; } = [];
        internal Exception? Failure { get; set; }
        internal int CreateCalls, FindCalls, RangeCalls;
        internal Guid TeacherId;
        internal CancellationToken Token;
        internal DateTime From, To;
        internal Guid[] VisibleIds = [];
        public Task<Session?> FindAsync(Guid id, CancellationToken ct)
        { FindCalls++; return Task.FromResult(Items.SingleOrDefault(x => x.Id == id)); }
        public Task<IReadOnlyList<Session>> RangeAsync(DateTime fromUtc, DateTime toUtc, IReadOnlyCollection<Guid> visibleClassIds, CancellationToken ct)
        {
            RangeCalls++; From = fromUtc; To = toUtc; VisibleIds = visibleClassIds.ToArray();
            return Task.FromResult<IReadOnlyList<Session>>(Items.Where(x => visibleClassIds.Contains(x.ClassId)).ToArray());
        }
        public Task CreateWithoutTeacherOverlapAsync(Session session, Guid teacherId, CancellationToken ct)
        {
            CreateCalls++; TeacherId = teacherId; Token = ct;
            if (Failure is not null) return Task.FromException(Failure);
            ct.ThrowIfCancellationRequested(); Items.Add(session); return Task.CompletedTask;
        }
        public Task<Session> ChangeAsync(Guid id, Guid classId, Guid teacherId, int expectedRevision, Session? replacement, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
