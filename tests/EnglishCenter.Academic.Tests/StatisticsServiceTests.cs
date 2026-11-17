using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application;
using EnglishCenter.Modules.Academic.Application.Statistics;
using EnglishCenter.Modules.Academic.Contracts.Statistics;
using Microsoft.Extensions.Logging;
using Xunit;
using static EnglishCenter.Academic.Tests.SessionDomainTests;

namespace EnglishCenter.Academic.Tests;

public sealed class StatisticsServiceTests
{
    private readonly FakeAcademicStore classes = new();
    private readonly StatsStore store = new();
    private readonly FakeClassPolicy policy;
    private readonly StatsClock clock = new(new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly StatsLogger logger = new();

    public StatisticsServiceTests()
    {
        policy = new(classes);
        policy.As(Roles.Student);
    }

    private StatisticsService Service(params IPersonalStatsContributor[] contributors) =>
        new(store, policy.Access, policy, clock, contributors, logger);

    [Fact]
    public async Task MissingProviders_AlwaysProduceOrderedExplicitUnavailableSectionsAndRealZero()
    {
        var result = await Service().MineAsync("2027-01", default);
        Assert.Equal("partial", result.Availability);
        Assert.Equal("UTC+07:00", result.Timezone);
        Assert.Equal(new[] { "assessment", "warnings", "feedback" }, result.Contributors.Select(x => x.Key));
        Assert.All(result.Contributors, x =>
        {
            Assert.Equal("unavailable", x.Availability);
            Assert.Equal("not_registered", x.Reason);
            Assert.Null(x.Metrics);
        });
        Assert.Equal(0, result.Academic.CurrentClassCount);
        Assert.Equal(0, result.Academic.Attendance.RecordedCount);
        Assert.Null(result.Academic.CompletedSessionsChangePercent);
        Assert.Equal(Enumerable.Range(1, 12).Select(x => $"2027-{x:D2}"), result.Chart.Select(x => x.Month));
        Assert.Equal(1, store.ReadCalls);
    }

    [Fact]
    public async Task Providers_ReceiveSameRawInstantAndHalfOpenVietnamPeriods_WithFrozenMetricsAndNullMeans()
    {
        clock.Now = clock.Now.AddTicks(7);
        var assessment = new Contributor("assessment", new([new("gradedSubmissionCount", 1, 8), new("averageGrade", null, null)]));
        var warnings = new Contributor("warnings", new([new("issuedCount", 0, 0), new("activeCount", 1, 0)]));
        var feedback = new Contributor("feedback", new([new("submittedCount", 9, 8)]));
        var result = await Service(feedback, warnings, assessment).MineAsync("2027-01", default);
        Assert.Equal("complete", result.Availability);
        Assert.All(result.Contributors, x => { Assert.Equal("available", x.Availability); Assert.Null(x.Reason); });
        Assert.Equal(-88m, result.Contributors[0].Metrics![0].ChangePercent);
        Assert.Null(result.Contributors[0].Metrics![1].Current);
        Assert.Null(result.Contributors[0].Metrics![1].ChangePercent);
        Assert.All(result.Contributors[1].Metrics!, x => Assert.Null(x.ChangePercent));
        Assert.Equal(13m, result.Contributors[2].Metrics![0].ChangePercent);
        var period = Assert.IsType<StatsPeriod>(assessment.Period);
        Assert.Equal(policy.UserId, period.StudentId);
        Assert.Equal(DateTimeOffset.Parse("2026-12-31T17:00:00Z"), period.CurrentFromUtc);
        Assert.Equal(DateTimeOffset.Parse("2027-01-31T17:00:00Z"), period.CurrentToUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-30T17:00:00Z"), period.PreviousFromUtc);
        Assert.Equal(period.CurrentFromUtc, period.PreviousToUtc);
        Assert.Equal(clock.Now, period.AsOfUtc);
        Assert.Equal(period, warnings.Period);
        Assert.Equal(period, feedback.Period);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(0, result.AsOfUtc.Ticks % 10);
    }

    [Fact]
    public async Task FailedOrMalformedProvider_IsLoggedAndDoesNotHideGoodPeers()
    {
        var failed = new Contributor("assessment", new([])) { Failure = new InvalidOperationException("synthetic private failure") };
        var malformed = new Contributor("warnings", new([new("issuedCount", 1, 0)]));
        var good = new Contributor("feedback", new([new("submittedCount", 3, 1)]));
        var result = await Service(failed, malformed, good).MineAsync("2027-01", default);
        Assert.Equal("partial", result.Availability);
        Assert.All(result.Contributors.Take(2), x =>
        {
            Assert.Equal("provider_failed", x.Reason);
            Assert.Null(x.Metrics);
        });
        Assert.Equal(3m, Assert.Single(result.Contributors[2].Metrics!).Current);
        Assert.Equal(2, logger.Failures.Count);
        Assert.Contains(failed.Failure, logger.Failures);
    }

    [Fact]
    public async Task ProviderCancellation_PropagatesInsteadOfBecomingPartial()
    {
        var failure = new OperationCanceledException();
        var provider = new Contributor("assessment", new([])) { Failure = failure };
        Assert.Same(failure, await Record.ExceptionAsync(() => Service(provider).MineAsync("2027-01", default)));
        Assert.Empty(logger.Failures);
    }

    [Fact]
    public void DuplicateProviderKeys_AreConfigurationFailure()
    {
        var provider = new Contributor("feedback", new([new("submittedCount", 0, 0)]));
        Assert.Throws<InvalidOperationException>(() => Service(provider, provider));
    }

    [Fact]
    public async Task AcademicQueryFailure_PropagatesBeforeProvidersInsteadOfInventingZero()
    {
        store.Failure = new InvalidOperationException("own schema unavailable");
        var provider = new Contributor("feedback", new([new("submittedCount", 0, 0)]));
        Assert.Same(store.Failure, await Record.ExceptionAsync(() => Service(provider).MineAsync("2027-01", default)));
        Assert.Null(provider.Period);
    }

    [Fact]
    public async Task Statistics_UsesCurrentClassScopeAndOwnRetainedAttendance_AndSameChartBuckets()
    {
        var visible = NewClass(Guid.NewGuid());
        classes.Classes.AddRange([visible, NewClass(Guid.NewGuid(), "B")]);
        policy.Enrollments.Add((visible.Id, policy.UserId!.Value));
        var end = DateTime.Parse("2027-01-01T11:00:00Z").ToUniversalTime();
        clock.Now = new DateTimeOffset(end.AddTicks(1));
        store.Data = new([
            new(DateTime.Parse("2026-12-31T16:59:59Z").ToUniversalTime(), end.AddMinutes(-1)),
            new(DateTime.Parse("2026-12-31T17:00:00Z").ToUniversalTime(), end),
            new(DateTime.Parse("2027-01-31T17:00:00Z").ToUniversalTime(), end.AddMonths(1))
        ], [new(end, "AbsentExcused"), new(end, "AbsentUnexcused"), new(end, "Late"),
            new(end, "LateOver20Min"), new(end, "LeftEarly"), new(end, "Present")]);
        var result = await Service().MineAsync("2027-01", default);
        Assert.Equal(new[] { visible.Id }, store.VisibleIds);
        Assert.Equal(policy.UserId, store.StudentId);
        Assert.Equal(1, result.Academic.CurrentClassCount);
        Assert.Equal(1, result.Academic.CurrentCompletedSessions);
        Assert.Equal(1, result.Academic.PreviousCompletedSessions);
        Assert.Equal(0m, result.Academic.CompletedSessionsChangePercent);
        var attendance = result.Academic.Attendance;
        Assert.Equal(6, attendance.RecordedCount);
        Assert.Equal(2, attendance.AbsentCount);
        Assert.Equal(2, attendance.LateCount);
        Assert.Equal(1, attendance.LeftEarlyCount);
        Assert.Equal(new StatusCounts(1, 1, 1, 1, 1, 1), attendance.Counts);
        Assert.Equal(new StatusPercents(17, 17, 17, 17, 17, 17), attendance.Percentages);
        Assert.Equal(attendance.Counts, result.Chart[0].Counts);
        Assert.Equal(attendance.RecordedCount, result.Chart[0].RecordedAttendance);
        Assert.Equal(result.Academic.CurrentCompletedSessions, result.Chart[0].CompletedSessions);
        Assert.All(result.Chart.Skip(1), x => Assert.Equal(0, x.CompletedSessions));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task Progress_UsesRawStrictEndAndWholeAwayFromZeroPercent(long deltaTicks, int completed)
    {
        var item = NewClass(Guid.NewGuid()); classes.Classes.Add(item);
        policy.Enrollments.Add((item.Id, policy.UserId!.Value));
        var end = new DateTime(2027, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        store.Ends = [end, end.AddDays(1), end.AddDays(2)];
        clock.Now = new DateTimeOffset(end.AddTicks(deltaTicks));
        var result = await Service().ProgressAsync(item.Id, default);
        Assert.Equal(3, result.TotalSessions);
        Assert.Equal(completed, result.CompletedSessions);
        Assert.Equal(3 - completed, result.RemainingSessions);
        Assert.Equal(completed == 0 ? 0 : 33, result.CompletionPercent);
        Assert.Equal(1, clock.Reads);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task StatisticsAndChart_UseSameStrictRawClockBoundary(long deltaTicks, int completed)
    {
        var end = new DateTime(2027, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        clock.Now = new DateTimeOffset(end.AddTicks(deltaTicks));
        store.Data = new([new(end.AddHours(-1), end)], []);
        var result = await Service().MineAsync("2027-01", default);
        Assert.Equal(completed, result.Academic.CurrentCompletedSessions);
        Assert.Equal(completed, result.Chart[0].CompletedSessions);
    }

    [Fact]
    public async Task Progress_EmptyClassIsZero_AndRevocationDeniesReadBeforeStore()
    {
        var item = NewClass(Guid.NewGuid()); classes.Classes.Add(item);
        policy.Enrollments.Add((item.Id, policy.UserId!.Value));
        Assert.Equal(0, (await Service().ProgressAsync(item.Id, default)).CompletionPercent);
        policy.Enrollments.Clear();
        var calls = store.ProgressCalls;
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().ProgressAsync(item.Id, default))).Status);
        Assert.Equal(calls, store.ProgressCalls);
    }

    [Fact]
    public async Task Percentages_RoundHalfAwayFromZero_ForProgressAndActualAttendance()
    {
        var item = NewClass(Guid.NewGuid()); classes.Classes.Add(item);
        policy.Enrollments.Add((item.Id, policy.UserId!.Value));
        var start = new DateTime(2027, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        store.Ends = [start, .. Enumerable.Repeat(start.AddDays(1), 7)];
        Assert.Equal(13, (await Service().ProgressAsync(item.Id, default)).CompletionPercent);
        store.Data = new([], [new(start, "Present"), .. Enumerable.Repeat(new StatisticsAttendance(start, "Late"), 7)]);
        var summary = (await Service().MineAsync("2027-01", default)).Academic.Attendance;
        Assert.Equal(8, summary.RecordedCount);
        Assert.Equal(13, summary.Percentages.Present);
        Assert.Equal(88, summary.Percentages.Late);
    }

    [Fact]
    public async Task EmptyClassIdAndMissingClass_HaveDistinctFailuresBeforeStore()
    {
        Assert.Equal(400, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().ProgressAsync(Guid.Empty, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().ProgressAsync(Guid.NewGuid(), default))).Status);
        Assert.Equal(0, store.ProgressCalls);
    }

    [Theory]
    [InlineData("2027-13")]
    [InlineData("2027-1")]
    [InlineData("0001-12")]
    [InlineData("9999-01")]
    [InlineData("")]
    [InlineData(" 2027-01")]
    [InlineData("٢٠٢٧-٠١")]
    public async Task InvalidMonth_IsValidationFailureBeforeQuery(string month)
    {
        var failure = await Assert.ThrowsAsync<AcademicFailure>(() => Service().MineAsync(month, default));
        Assert.Equal(400, failure.Status); Assert.Contains("month", failure.Errors!.Keys);
        Assert.Equal(0, store.ReadCalls);
    }

    [Theory]
    [InlineData("0002-01", "0001-12-31T17:00:00Z")]
    [InlineData("9998-12", "9998-11-30T17:00:00Z")]
    [InlineData("2028-03", "2028-02-29T17:00:00Z")]
    public async Task MonthBounds_AcceptSupportedExtremesAndLeapYear(string month, string expectedFrom)
    {
        var provider = new Contributor("feedback", new([new("submittedCount", 0, 0)]));
        var result = await Service(provider).MineAsync(month, default);
        Assert.Equal(month, result.Month);
        Assert.Equal(DateTimeOffset.Parse(expectedFrom), provider.Period!.CurrentFromUtc);
        Assert.Equal(12, result.Chart.Count);
    }

    [Fact]
    public async Task DefaultMonth_IsVietnamMonth_NotUtcMonth()
    {
        clock.Now = DateTimeOffset.Parse("2026-12-31T17:00:00Z");
        Assert.Equal("2027-01", (await Service().MineAsync(null, default)).Month);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    public async Task PersonalStatistics_RejectsManagerAndTeacherImpersonation(string role)
    {
        policy.As(role);
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().MineAsync("2027-01", default))).Status);
        Assert.Equal(0, store.ReadCalls);
    }

    [Fact]
    public async Task AuthenticationAndLiveAccount_PrecedeValidationAndQueries()
    {
        policy.IsAuthenticated = false;
        Assert.Equal(401, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().MineAsync("bad", default))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().ProgressAsync(Guid.Empty, default))).Status);
        policy.IsAuthenticated = true; policy.Accounts.Clear();
        Assert.Equal(403, (await Assert.ThrowsAsync<AcademicFailure>(() => Service().MineAsync("2027-01", default))).Status);
        Assert.Equal(0, store.ReadCalls);
    }

    private sealed class StatsStore : IStatisticsStore
    {
        public StatisticsData Data = new([], []);
        public IReadOnlyList<DateTime> Ends = [];
        public Exception? Failure;
        public Guid[] VisibleIds = [];
        public Guid StudentId;
        public int ReadCalls, ProgressCalls;
        public Task<IReadOnlyList<DateTime>> SessionEndsAsync(Guid classId, CancellationToken ct)
        {
            ProgressCalls++; ct.ThrowIfCancellationRequested(); return Task.FromResult(Ends);
        }
        public Task<StatisticsData> ReadAsync(Guid studentId, IReadOnlyCollection<Guid> visibleClassIds,
            DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            ReadCalls++; StudentId = studentId; VisibleIds = visibleClassIds.ToArray();
            ct.ThrowIfCancellationRequested();
            return Failure is null ? Task.FromResult(Data) : Task.FromException<StatisticsData>(Failure);
        }
    }

    private sealed class Contributor(string key, StatsContribution contribution) : IPersonalStatsContributor
    {
        public string Key => key;
        public StatsPeriod? Period;
        public Exception? Failure;
        public Task<StatsContribution> ReadAsync(StatsPeriod period, CancellationToken ct)
        {
            Period = period; ct.ThrowIfCancellationRequested();
            return Failure is null ? Task.FromResult(contribution) : Task.FromException<StatsContribution>(Failure);
        }
    }

    private sealed class StatsClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public int Reads;
        public override DateTimeOffset GetUtcNow() { Reads++; return Now; }
    }

    private sealed class StatsLogger : ILogger<StatisticsService>
    {
        public List<Exception?> Failures { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Failures.Add(exception);
    }
}
