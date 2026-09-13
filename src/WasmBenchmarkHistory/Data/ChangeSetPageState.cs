namespace WasmBenchmarkHistory.Data;

public enum ChangeSetSort
{
    Magnitude,
    Name,
    ReportedQuality
}

public enum ChangeSetQualityFilter
{
    All,
    Strong,
    Review,
    NoiseRisk
}

public sealed record ChangeSetPageState(
    int? IssueNumber,
    string Search,
    ChangeSetSort Sort,
    ChangeSetQualityFilter Quality);

public static class ChangeSetPageStateCodec
{
    private const int MaximumSearchLength = 256;

    public static ChangeSetPageState Parse(string uri)
    {
        var queryStart = uri.IndexOf('?');
        if (queryStart < 0)
        {
            return Default;
        }

        var fragment = uri.IndexOf('#', queryStart);
        var query = uri[(queryStart + 1)..(fragment < 0 ? uri.Length : fragment)];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Decode(separator < 0 ? pair : pair[..separator]);
            var value = Decode(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            if (!values.TryAdd(key, value))
            {
                return Default;
            }
        }

        int? issue = null;
        if (values.TryGetValue("issue", out var issueValue)
            && int.TryParse(issueValue, out var number)
            && number > 0)
        {
            issue = number;
        }

        var search = values.GetValueOrDefault("search") ?? string.Empty;
        if (search.Length > MaximumSearchLength)
        {
            search = string.Empty;
        }

        var sort = values.GetValueOrDefault("sort") switch
        {
            "name" => ChangeSetSort.Name,
            "quality" => ChangeSetSort.ReportedQuality,
            _ => ChangeSetSort.Magnitude
        };
        var quality = values.GetValueOrDefault("quality") switch
        {
            "strong" => ChangeSetQualityFilter.Strong,
            "review" => ChangeSetQualityFilter.Review,
            "noise" => ChangeSetQualityFilter.NoiseRisk,
            _ => ChangeSetQualityFilter.All
        };
        return new ChangeSetPageState(issue, search, sort, quality);
    }

    public static string ToRelativeUri(ChangeSetPageState state)
    {
        var values = new List<string>();
        if (state.IssueNumber is { } issue)
        {
            values.Add($"issue={issue}");
        }

        if (state.Search.Length > 0)
        {
            values.Add($"search={Uri.EscapeDataString(state.Search)}");
        }

        values.Add($"sort={state.Sort switch
        {
            ChangeSetSort.Name => "name",
            ChangeSetSort.ReportedQuality => "quality",
            _ => "magnitude"
        }}");
        values.Add($"quality={state.Quality switch
        {
            ChangeSetQualityFilter.Strong => "strong",
            ChangeSetQualityFilter.Review => "review",
            ChangeSetQualityFilter.NoiseRisk => "noise",
            _ => "all"
        }}");
        return "/change-set?" + string.Join('&', values);
    }

    private static ChangeSetPageState Default =>
        new(null, string.Empty, ChangeSetSort.Magnitude, ChangeSetQualityFilter.All);

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }
}
