using System.Globalization;

namespace WasmBenchmarkHistory.Data;

public sealed record HistoryPageState(
    string? Benchmark,
    IReadOnlyList<string> RunIds,
    bool Normalized,
    HistoryTimeRange TimeRange,
    string? InvestigationRunId,
    ObservationIdentity? BaselineA,
    ObservationIdentity? ComparisonB,
    bool UseRobustSummary,
    bool ShowVariabilityBand = false,
    int VariabilityWindow = RollingVariabilityCalculator.DefaultWindow,
    IReadOnlyList<string>? SelectedBenchmarks = null,
    IReadOnlyList<string>? FocusedRunIds = null,
    string? BaselineRunId = null,
    string? ComparisonRunId = null);

public sealed record HistoryPageStateParseResult(
    HistoryPageState State,
    IReadOnlyList<string> Warnings);

public static class HistoryPageStateCodec
{
    private const int MaximumBenchmarkLength = 512;

    public static HistoryPageStateParseResult Parse(string uri)
    {
        var warnings = new List<string>();
        IReadOnlyDictionary<string, IReadOnlyList<string>> values;
        try
        {
            values = ParseQuery(uri);
        }
        catch (UriFormatException)
        {
            warnings.Add("The shared query string was malformed and was ignored.");
            values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        var benchmark = GetSingle(values, "benchmark", warnings);
        if (benchmark is not null
            && (string.IsNullOrWhiteSpace(benchmark) || benchmark.Length > MaximumBenchmarkLength))
        {
            warnings.Add("The shared benchmark name is invalid and was ignored.");
            benchmark = null;
        }

        var selectedBenchmarks = new List<string>();
        if (values.TryGetValue("selected", out var selected))
        {
            foreach (var name in selected.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumBenchmarkLength)
                {
                    warnings.Add("An invalid shared benchmark selection was ignored.");
                }
                else
                {
                    selectedBenchmarks.Add(name);
                }
            }
        }

        benchmark ??= selectedBenchmarks.FirstOrDefault();
        if (benchmark is not null && !selectedBenchmarks.Contains(benchmark, StringComparer.Ordinal))
        {
            selectedBenchmarks.Insert(0, benchmark);
        }

        var runIds = ParseRuns(GetSingle(values, "runs", warnings), warnings);
        var focusedRunIds = ParseFocus(GetSingle(values, "focus", warnings), runIds, warnings);
        var normalized = ParseMode(GetSingle(values, "mode", warnings), warnings);
        var timeRange = ParseRange(values, warnings);
        var investigationRunId = GetSingle(values, "investigate", warnings);
        if (investigationRunId is not null
            && !KnownRunConfigurations.All.Any(run => run.Id == investigationRunId))
        {
            warnings.Add("The shared investigation run is unknown and was ignored.");
            investigationRunId = null;
        }

        var baseline = ParsePin(
            GetSingle(values, "a", warnings),
            "baseline A",
            investigationRunId,
            warnings);
        var comparison = ParsePin(
            GetSingle(values, "b", warnings),
            "comparison B",
            investigationRunId,
            warnings);
        var robust = ParseSummary(GetSingle(values, "summary", warnings), warnings);
        var showVariabilityBand = ParseBand(
            GetSingle(values, "band", warnings),
            warnings);
        var variabilityWindow = ParseBandWindow(
            GetSingle(values, "bandWindow", warnings),
            warnings);

        return new HistoryPageStateParseResult(
            new HistoryPageState(
                benchmark,
                runIds,
                normalized,
                timeRange,
                investigationRunId,
                baseline.Identity,
                comparison.Identity,
                robust,
                showVariabilityBand,
                variabilityWindow,
                selectedBenchmarks,
                focusedRunIds,
                baseline.RunId,
                comparison.RunId),
            warnings);
    }

    public static string ToRelativeUri(HistoryPageState state)
    {
        var values = new List<KeyValuePair<string, string>>();
        Add(values, "benchmark", state.Benchmark);
        if (state.SelectedBenchmarks is { Count: > 1 })
        {
            foreach (var benchmark in state.SelectedBenchmarks.Distinct(StringComparer.Ordinal))
            {
                Add(values, "selected", benchmark);
            }
        }
        if (state.RunIds.Count > 0)
        {
            Add(values, "runs", string.Join(',', state.RunIds));
        }

        if (state.FocusedRunIds is { Count: > 0 })
        {
            Add(values, "focus", string.Join(',', state.FocusedRunIds.Distinct(StringComparer.Ordinal)));
        }

        Add(values, "mode", state.Normalized ? "normalized" : "raw");
        Add(values, "range", RangeToken(state.TimeRange.Kind));
        if (state.TimeRange.Kind == HistoryRangeKind.Custom)
        {
            Add(values, "from", state.TimeRange.Start?.Ticks.ToString(CultureInfo.InvariantCulture));
            Add(values, "to", state.TimeRange.End?.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        Add(values, "investigate", state.InvestigationRunId);
        Add(values, "a", FormatPin(state.BaselineA, state.BaselineRunId));
        Add(values, "b", FormatPin(state.ComparisonB, state.ComparisonRunId));
        Add(values, "summary", state.UseRobustSummary ? "robust" : "point");
        if (state.ShowVariabilityBand)
        {
            Add(values, "band", "iqr");
            Add(
                values,
                "bandWindow",
                state.VariabilityWindow.ToString(CultureInfo.InvariantCulture));
        }

        return values.Count == 0
            ? "/"
            : "/?" + string.Join(
                '&',
                values.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseQuery(string uri)
    {
        var queryStart = uri.IndexOf('?');
        if (queryStart < 0 || queryStart == uri.Length - 1)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        var fragmentStart = uri.IndexOf('#', queryStart);
        var query = uri[
            (queryStart + 1)..(fragmentStart < 0 ? uri.Length : fragmentStart)];
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Decode(separator < 0 ? pair : pair[..separator]);
            var value = Decode(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            if (!values.TryGetValue(key, out var existing))
            {
                existing = [];
                values.Add(key, existing);
            }

            existing.Add(value);
        }

        return values.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static string? GetSingle(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        string key,
        List<string> warnings)
    {
        if (!values.TryGetValue(key, out var matches))
        {
            return null;
        }

        if (matches.Count != 1)
        {
            warnings.Add($"The shared '{key}' value was repeated and was ignored.");
            return null;
        }

        return matches[0];
    }

    private static IReadOnlyList<string> ParseRuns(string? value, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var runIds = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (runIds.Length < 2 || runIds.Length > KnownRunConfigurations.All.Count
            || runIds.Any(id => !KnownRunConfigurations.All.Any(run => run.Id == id)))
        {
            warnings.Add("The shared run selection is invalid and was ignored.");
            return [];
        }

        return runIds;
    }

    private static IReadOnlyList<string> ParseFocus(
        string? value,
        IReadOnlyList<string> runIds,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var focused = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (focused.Any(id => !KnownRunConfigurations.All.Any(run => run.Id == id))
            || runIds.Count > 0 && focused.Any(id => !runIds.Contains(id, StringComparer.Ordinal)))
        {
            warnings.Add("The shared run focus is invalid and was ignored.");
            return [];
        }

        return focused;
    }

    private static bool ParseMode(string? value, List<string> warnings)
    {
        if (value is null or "raw")
        {
            return false;
        }

        if (value == "normalized")
        {
            return true;
        }

        warnings.Add("The shared chart mode is invalid; raw mode was used.");
        return false;
    }

    private static bool ParseSummary(string? value, List<string> warnings)
    {
        if (value is null or "point")
        {
            return false;
        }

        if (value == "robust")
        {
            return true;
        }

        warnings.Add("The shared investigation summary mode is invalid; point mode was used.");
        return false;
    }

    private static bool ParseBand(string? value, List<string> warnings)
    {
        if (value is null or "off")
        {
            return false;
        }

        if (value == "iqr")
        {
            return true;
        }

        warnings.Add("The shared variability band mode is invalid; bands were disabled.");
        return false;
    }

    private static int ParseBandWindow(string? value, List<string> warnings)
    {
        if (value is null)
        {
            return RollingVariabilityCalculator.DefaultWindow;
        }

        if (int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var window)
            && RollingVariabilityCalculator.IsSupportedWindow(window))
        {
            return window;
        }

        warnings.Add(
            $"The shared variability window is invalid; {RollingVariabilityCalculator.DefaultWindow} observations were used.");
        return RollingVariabilityCalculator.DefaultWindow;
    }

    private static HistoryTimeRange ParseRange(
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        List<string> warnings)
    {
        var range = GetSingle(values, "range", warnings);
        if (range is null or "all")
        {
            return HistoryTimeRange.All;
        }

        if (range == "30d")
        {
            return HistoryTimeRange.ThirtyDays;
        }

        if (range == "90d")
        {
            return HistoryTimeRange.NinetyDays;
        }

        if (range == "1y")
        {
            return HistoryTimeRange.OneYear;
        }

        if (range == "custom")
        {
            var from = ParseTicks(GetSingle(values, "from", warnings));
            var to = ParseTicks(GetSingle(values, "to", warnings));
            if (from is not null && to is not null && from < to)
            {
                return HistoryTimeRange.Custom(from.Value, to.Value);
            }
        }

        warnings.Add("The shared time range is invalid; all history was used.");
        return HistoryTimeRange.All;
    }

    private static (ObservationIdentity? Identity, string? RunId) ParsePin(
        string? value,
        string label,
        string? investigationRunId,
        List<string> warnings)
    {
        if (value is null)
        {
            return (null, null);
        }

        var parts = value.Split('.', StringSplitOptions.None);
        var hasRunId = parts.Length == 4;
        var runId = hasRunId ? parts[0] : investigationRunId;
        var timestamp = ParseTicks(parts[hasRunId ? 1 : 0]);
        var runtimeSha = parts.Length switch
        {
            3 => parts[1],
            4 => parts[2],
            _ => null
        };
        var performanceSha = parts.Length switch
        {
            3 => parts[2],
            4 => parts[3],
            _ => null
        };
        if (runId is null)
        {
            warnings.Add($"The shared {label} pin had no valid source run and was ignored.");
            return (null, null);
        }

        if (!KnownRunConfigurations.All.Any(run => run.Id == runId)
            || timestamp is null
            || runtimeSha is null
            || performanceSha is null
            || !GitHubCompareLinkBuilder.IsValidSha(runtimeSha)
            || !GitHubCompareLinkBuilder.IsValidSha(performanceSha))
        {
            warnings.Add($"The shared {label} pin is invalid and was ignored.");
            return (null, null);
        }

        return (
            new ObservationIdentity(timestamp.Value, runtimeSha, performanceSha),
            runId);
    }

    private static DateTime? ParseTicks(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks
            || ticks > DateTime.MaxValue.Ticks)
        {
            return null;
        }

        return new DateTime(ticks, DateTimeKind.Unspecified);
    }

    private static string RangeToken(HistoryRangeKind kind) => kind switch
    {
        HistoryRangeKind.ThirtyDays => "30d",
        HistoryRangeKind.NinetyDays => "90d",
        HistoryRangeKind.OneYear => "1y",
        HistoryRangeKind.All => "all",
        HistoryRangeKind.Custom => "custom",
        _ => "all"
    };

    private static string? FormatPin(ObservationIdentity? identity, string? runId) =>
        identity is null
            || !GitHubCompareLinkBuilder.IsValidSha(identity.Value.RuntimeSha)
            || !GitHubCompareLinkBuilder.IsValidSha(identity.Value.PerformanceSha)
            ? null
            : string.Join(
                '.',
                new[]
                {
                    runId,
                    identity.Value.Timestamp.Ticks.ToString(CultureInfo.InvariantCulture),
                    identity.Value.RuntimeSha,
                    identity.Value.PerformanceSha
                }.Where(part => !string.IsNullOrEmpty(part)));

    private static string Decode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            if (index + 2 >= value.Length
                || !Uri.IsHexDigit(value[index + 1])
                || !Uri.IsHexDigit(value[index + 2]))
            {
                throw new UriFormatException("The query contains invalid percent encoding.");
            }

            index += 2;
        }

        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    private static void Add(
        ICollection<KeyValuePair<string, string>> values,
        string key,
        string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            values.Add(KeyValuePair.Create(key, value));
        }
    }
}
