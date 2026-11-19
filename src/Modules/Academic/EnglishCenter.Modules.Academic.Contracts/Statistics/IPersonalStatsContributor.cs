namespace EnglishCenter.Modules.Academic.Contracts.Statistics;

public interface IPersonalStatsContributor
{
    string Key { get; }
    Task<StatsContribution> ReadAsync(StatsPeriod period, CancellationToken ct);
}

public sealed record StatsPeriod(Guid StudentId, DateTimeOffset CurrentFromUtc, DateTimeOffset CurrentToUtc,
    DateTimeOffset PreviousFromUtc, DateTimeOffset PreviousToUtc, DateTimeOffset AsOfUtc);

public sealed record StatsContribution(IReadOnlyList<StatsMetric> Metrics);
public sealed record StatsMetric(string Key, decimal? Current, decimal? Previous);
