namespace WasmBenchmarkHistory.Data;

public static class ObservationMatcher
{
    public static IReadOnlyList<MatchedComparison> MatchStrict(
        IReadOnlyList<BenchmarkHistory> histories)
    {
        if (histories.Count < 2)
        {
            return [];
        }

        var uniqueByRun = histories.ToDictionary(
            history => history.Run.Id,
            history => history.Observations
                .GroupBy(ToKey)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single()),
            StringComparer.Ordinal);

        var matchingKeys = uniqueByRun.Values
            .Select(observations => observations.Keys.AsEnumerable())
            .Aggregate((left, right) => left.Intersect(right))
            .OrderBy(key => key.Timestamp)
            .ThenBy(key => key.RuntimeSha, StringComparer.Ordinal)
            .ThenBy(key => key.PerformanceSha, StringComparer.Ordinal);

        return matchingKeys
            .Select(key => new MatchedComparison(
                key,
                histories.ToDictionary(
                    history => history.Run.Id,
                    history => uniqueByRun[history.Run.Id][key],
                    StringComparer.Ordinal)))
            .ToArray();
    }

    private static ObservationKey ToKey(BenchmarkObservation observation) =>
        new(
            observation.Timestamp,
            observation.RuntimeSha,
            observation.PerformanceSha);
}
