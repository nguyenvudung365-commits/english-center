using System.Globalization;
using EnglishCenter.BuildingBlocks.Security;
using EnglishCenter.Modules.Academic.Application.Classes;
using EnglishCenter.Modules.Academic.Contracts.Statistics;
using Microsoft.Extensions.Logging;

namespace EnglishCenter.Modules.Academic.Application.Statistics;

public sealed class StatisticsService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly (string Key, string[] Metrics)[] Sections =
    [
        ("assessment", ["gradedSubmissionCount", "averageGrade"]),
        ("warnings", ["issuedCount", "activeCount"]),
        ("feedback", ["submittedCount"])
    ];
    private readonly IStatisticsStore store;
    private readonly ClassAccess access;
    private readonly ICurrentUser current;
    private readonly TimeProvider clock;
    private readonly IReadOnlyDictionary<string, IPersonalStatsContributor> contributors;
    private readonly ILogger<StatisticsService> logger;

    public StatisticsService(IStatisticsStore store, ClassAccess access, ICurrentUser current, TimeProvider clock,
        IEnumerable<IPersonalStatsContributor> contributors, ILogger<StatisticsService> logger)
    {
        this.store = store; this.access = access; this.current = current; this.clock = clock; this.logger = logger;
        var providers = new Dictionary<string, IPersonalStatsContributor>(StringComparer.Ordinal);
        foreach (var provider in contributors)
        {
            if (!Sections.Any(x => x.Key == provider.Key))
                throw new InvalidOperationException($"Unknown personal statistics contributor key '{provider.Key}'.");
            if (!providers.TryAdd(provider.Key, provider))
                throw new InvalidOperationException($"Duplicate personal statistics contributor key '{provider.Key}'.");
        }
        this.contributors = providers;
    }

    public async Task<ClassProgress> ProgressAsync(Guid classId, CancellationToken ct)
    {
        await access.RequireUserAsync(ct);
        if (classId == Guid.Empty) throw Invalid("classId", "Class ID must not be empty.");
        await access.RequireReadAsync(classId, ct);
        var asOf = clock.GetUtcNow();
        var ends = await store.SessionEndsAsync(classId, ct);
        var completed = ends.Count(x => x < asOf.UtcDateTime);
        return new(classId, PublicInstant(asOf), ends.Count, completed, ends.Count - completed, Percent(completed, ends.Count));
    }

    public async Task<PersonalStatistics> MineAsync(string? month, CancellationToken ct)
    {
        var studentId = await access.RequireUserAsync(ct);
        if (current.Role != Roles.Student) throw new AcademicFailure(403, "Only a Student can read personal statistics.");
        var asOf = clock.GetUtcNow();
        var selected = ParseMonth(month, asOf);
        var from = selected.ToUniversalTime();
        var to = selected.AddMonths(1).ToUniversalTime();
        var previousFrom = selected.AddMonths(-1).ToUniversalTime();
        var yearStart = new DateTimeOffset(selected.Year, 1, 1, 0, 0, 0, VietnamOffset).ToUniversalTime();
        var yearEnd = yearStart.AddYears(1);
        var visible = await access.VisibleClassesAsync(ct);
        var data = await store.ReadAsync(studentId, visible.Select(x => x.Id).ToArray(),
            (previousFrom < yearStart ? previousFrom : yearStart).UtcDateTime, yearEnd.UtcDateTime, ct);
        var currentCompleted = Completed(data, from, to, asOf);
        var previousCompleted = Completed(data, previousFrom, from, asOf);
        var academic = new AcademicStatistics(visible.Count, currentCompleted, previousCompleted,
            Change(currentCompleted, previousCompleted), Attendance(data, from, to));
        var chart = new List<MonthPoint>(12);
        for (var index = 0; index < 12; index++)
        {
            var start = yearStart.AddMonths(index);
            var end = yearStart.AddMonths(index + 1);
            var attendance = Attendance(data, start, end);
            chart.Add(new($"{selected.Year:D4}-{index + 1:D2}", Completed(data, start, end, asOf),
                attendance.RecordedCount, attendance.Counts));
        }
        var period = new StatsPeriod(studentId, from, to, previousFrom, from, asOf);
        var sections = new List<ContributorSection>(3);
        foreach (var section in Sections)
        {
            ct.ThrowIfCancellationRequested();
            if (!contributors.TryGetValue(section.Key, out var provider))
            {
                sections.Add(new(section.Key, "unavailable", "not_registered", null));
                continue;
            }
            try
            {
                var contribution = await provider.ReadAsync(period, ct);
                ct.ThrowIfCancellationRequested();
                if (contribution?.Metrics is not { } metrics || metrics.Count != section.Metrics.Length ||
                    section.Metrics.Any(key => metrics.Count(x => x is not null && x.Key == key) != 1))
                    throw new InvalidOperationException("Statistics provider returned an invalid metric contract.");
                sections.Add(new(section.Key, "available", null, section.Metrics.Select(key =>
                {
                    var metric = metrics.Single(x => x.Key == key);
                    return new ContributorMetric(key, metric.Current, metric.Previous, Change(metric.Current, metric.Previous));
                }).ToArray()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                logger.LogError(ex, "Personal statistics contributor {ContributorKey} failed for student {StudentId}.", section.Key, studentId);
                sections.Add(new(section.Key, "unavailable", "provider_failed", null));
            }
        }
        return new(FormatMonth(selected), "UTC+07:00", PublicInstant(asOf),
            sections.All(x => x.Availability == "available") ? "complete" : "partial", academic, sections, chart);
    }

    private static DateTimeOffset ParseMonth(string? month, DateTimeOffset asOf)
    {
        month ??= FormatMonth(asOf.ToOffset(VietnamOffset));
        if (month.Length != 7 || month[4] != '-' ||
            month.Where((_, index) => index != 4).Any(x => x is < '0' or > '9') ||
            !int.TryParse(month.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(month.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            year is < 2 or > 9998 || number is < 1 or > 12)
            throw Invalid("month", "Month must be YYYY-MM between 0002-01 and 9998-12.");
        return new(year, number, 1, 0, 0, 0, VietnamOffset);
    }

    private static int Completed(StatisticsData data, DateTimeOffset from, DateTimeOffset to, DateTimeOffset asOf) =>
        data.Sessions.Count(x => x.StartsAtUtc >= from.UtcDateTime && x.StartsAtUtc < to.UtcDateTime && x.EndsAtUtc < asOf.UtcDateTime);

    private static Summary Attendance(StatisticsData data, DateTimeOffset from, DateTimeOffset to)
    {
        var counts = new int[6];
        foreach (var row in data.Attendance.Where(x => x.StartsAtUtc >= from.UtcDateTime && x.StartsAtUtc < to.UtcDateTime))
        {
            var index = row.Status switch
            {
                "Present" => 0, "AbsentUnexcused" => 1, "AbsentExcused" => 2,
                "Late" => 3, "LateOver20Min" => 4, "LeftEarly" => 5,
                _ => throw new InvalidOperationException("Unknown persisted Academic attendance status.")
            };
            counts[index]++;
        }
        var total = counts.Sum();
        var percentages = counts.Select(x => Percent(x, total)).ToArray();
        return new(total, new(counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]),
            new(percentages[0], percentages[1], percentages[2], percentages[3], percentages[4], percentages[5]),
            counts[1] + counts[2], counts[3] + counts[4], counts[5]);
    }

    private static int Percent(int count, int total) => total == 0 ? 0 : (int)Math.Round(count * 100m / total, MidpointRounding.AwayFromZero);
    private static decimal? Change(decimal? current, decimal? previous) => current is null || previous is null or 0
        ? null : Math.Round((current.Value - previous.Value) * 100m / previous.Value, MidpointRounding.AwayFromZero);
    private static string FormatMonth(DateTimeOffset month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    // Keep raw ticks for decisions; only the public timestamp is normalized to storage precision.
    private static DateTimeOffset PublicInstant(DateTimeOffset instant) => new(instant.UtcTicks - instant.UtcTicks % 10, TimeSpan.Zero);
    private static AcademicFailure Invalid(string field, string message) =>
        new(400, "One or more validation errors occurred.", new Dictionary<string, string[]> { [field] = [message] });
}
