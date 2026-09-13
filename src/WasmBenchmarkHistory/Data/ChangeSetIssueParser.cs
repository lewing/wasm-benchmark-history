using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WasmBenchmarkHistory.Data;

public sealed partial class ChangeSetIssueParser
{
    public ChangeSetDocument Parse(PerfIssuePayload issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var lines = issue.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var metadata = ParseMetadata(issue.Body);
        var groups = new List<ChangeSetGroup>();
        ChangeSetRunInformation? currentRun = null;

        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Equals("### Run Information", StringComparison.Ordinal))
            {
                currentRun = ParseRunInformation(lines, index + 1);
                continue;
            }

            var heading = GroupHeadingPattern().Match(lines[index]);
            if (!heading.Success)
            {
                continue;
            }

            if (currentRun is null)
            {
                throw SchemaError("A benchmark group did not have preceding run information.");
            }

            var end = FindGroupEnd(lines, index + 1);
            groups.Add(ParseGroup(lines, index, end, heading, currentRun));
            index = end - 1;
        }

        if (groups.Count == 0)
        {
            throw SchemaError("No improvement or regression benchmark groups were found.");
        }

        var triage = issue.Comments
            .Select(ParseExternalTriage)
            .FirstOrDefault(summary => summary is not null);
        return new ChangeSetDocument(
            issue.Number,
            issue.Title,
            issue.State,
            issue.HtmlUri,
            issue.Labels,
            metadata,
            groups,
            triage);
    }

    private static ChangeSetMetadata ParseMetadata(string body)
    {
        var match = DataPattern().Match(body);
        if (!match.Success)
        {
            throw SchemaError("Hidden DATA metadata was not found.");
        }

        try
        {
            using var document = JsonDocument.Parse(match.Groups["json"].Value);
            var root = document.RootElement;
            var runType = root.GetProperty("RunType");
            return new ChangeSetMetadata(
                RequiredString(runType, "Repo"),
                RequiredString(runType, "Branch"),
                RequiredString(runType, "Arch"),
                RequiredString(runType, "Os"),
                RequiredString(runType, "Queue"),
                RequiredString(runType, "Frequency"),
                runType.GetProperty("Configs")
                    .EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => item.Length > 0)
                    .ToArray(),
                root.TryGetProperty("RegressionDate", out var date)
                    && DateTime.TryParseExact(
                        date.GetString(),
                        "yyyy-MM-dd'T'HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var parsedDate)
                        ? parsedDate
                        : null,
                root.TryGetProperty("IsRegression", out var regression)
                    && regression.ValueKind is JsonValueKind.True,
                OptionalString(root, "PerformanceSha")
                    ?? OptionalString(root, "PerfRepoHash"));
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw SchemaError($"Hidden DATA metadata was invalid: {exception.Message}");
        }
    }

    private static ChangeSetRunInformation ParseRunInformation(
        IReadOnlyList<string> lines,
        int start)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("### ", StringComparison.Ordinal)
                || line.Equals("---", StringComparison.Ordinal))
            {
                break;
            }

            var separator = line.IndexOf('|');
            if (separator <= 0 || line.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        var baseline = ParseRuntimeSha(values.GetValueOrDefault("Baseline"), "Baseline");
        var compare = ParseRuntimeSha(values.GetValueOrDefault("Compare"), "Compare");
        if (baseline.Equals(compare, StringComparison.OrdinalIgnoreCase))
        {
            throw SchemaError("Baseline and compare runtime identities were the same.");
        }

        var diff = ParseLink(values.GetValueOrDefault("Diff"), "runtime diff");
        if (!SafeChangeSetLinks.IsRuntimeCompare(diff, baseline, compare))
        {
            throw SchemaError("The runtime diff link did not match the imported baseline and compare SHAs.");
        }

        return new ChangeSetRunInformation(
            Required(values, "Architecture"),
            Required(values, "OS"),
            Required(values, "Queue"),
            baseline,
            compare,
            diff,
            Required(values, "Configs"));
    }

    private static ChangeSetGroup ParseGroup(
        IReadOnlyList<string> lines,
        int start,
        int end,
        Match heading,
        ChangeSetRunInformation run)
    {
        var direction = heading.Groups["direction"].Value.Equals(
            "Improvements",
            StringComparison.Ordinal)
                ? ChangeDirection.Improvement
                : ChangeDirection.Regression;
        var name = WebUtility.HtmlDecode(heading.Groups["name"].Value.Trim());
        Uri? reportUri = null;
        string? repro = null;
        var rows = new List<ChangeSetBenchmark>();

        for (var index = start + 1; index < end; index++)
        {
            var line = lines[index].Trim();
            var reportMatch = ReportPattern().Match(line);
            if (reportMatch.Success)
            {
                var candidate = ParseAbsoluteUri(reportMatch.Groups["uri"].Value, "test report");
                if (!SafeChangeSetLinks.IsReport(candidate))
                {
                    throw SchemaError("A test report link used an unsupported host or path.");
                }

                reportUri = candidate;
            }

            if (line.Equals("### Repro", StringComparison.Ordinal))
            {
                repro = ParseRepro(lines, index + 1, end);
            }

            if (!line.StartsWith("|<ul>", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(ParseBenchmarkRow(line));
        }

        if (rows.Count == 0)
        {
            throw SchemaError($"Group '{name}' did not contain benchmark rows.");
        }

        return new ChangeSetGroup(
            name,
            direction,
            run,
            rows.Select(row => row with { ReportUri = reportUri }).ToArray(),
            repro);
    }

    private static ChangeSetBenchmark ParseBenchmarkRow(string line)
    {
        var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
        if (cells.Length < 6)
        {
            throw SchemaError("A benchmark table row did not have the expected columns.");
        }

        var links = MarkdownLinkPattern().Matches(cells[0]);
        if (links.Count == 0)
        {
            throw SchemaError("A benchmark row did not contain a history link.");
        }

        var history = ParseAbsoluteUri(links[0].Groups["uri"].Value, "history");
        if (!SafeChangeSetLinks.IsHistory(history))
        {
            throw SchemaError("A history link used an unsupported host or path.");
        }

        var target = ImportedHistoryTarget.Parse(history);
        Uri? source = null;
        foreach (Match link in links.Cast<Match>().Skip(1))
        {
            var candidate = ParseAbsoluteUri(link.Groups["uri"].Value, "benchmark source");
            if (link.Groups["text"].Value.Contains("Benchmark Source", StringComparison.OrdinalIgnoreCase))
            {
                if (!SafeChangeSetLinks.IsSource(candidate))
                {
                    throw SchemaError("A benchmark source link used an unsupported host or path.");
                }

                source = candidate;
            }
        }

        if (!double.TryParse(cells[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            || !double.IsFinite(ratio)
            || ratio <= 0)
        {
            throw SchemaError($"Reported ratio '{cells[3]}' was invalid.");
        }

        double? quality = null;
        if (cells[4].Length > 0)
        {
            if (!double.TryParse(
                    cells[4],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsedQuality)
                || !double.IsFinite(parsedQuality)
                || parsedQuality < 0)
            {
                throw SchemaError($"Reported test quality '{cells[4]}' was invalid.");
            }

            quality = parsedQuality;
        }

        bool? edge = cells[5].Length == 0
            ? null
            : bool.TryParse(cells[5], out var parsedEdge)
                ? parsedEdge
                : throw SchemaError($"Reported edge detector '{cells[5]}' was invalid.");
        var displayName = WebUtility.HtmlDecode(links[0].Groups["text"].Value);
        const string durationSuffix = " - Duration of single invocation";
        if (displayName.EndsWith(durationSuffix, StringComparison.Ordinal))
        {
            displayName = displayName[..^durationSuffix.Length];
        }

        return new ChangeSetBenchmark(
            target.Benchmark,
            displayName,
            WebUtility.HtmlDecode(cells[1]),
            WebUtility.HtmlDecode(cells[2]),
            ratio,
            quality,
            edge,
            history,
            source,
            null);
    }

    private static string? ParseRepro(
        IReadOnlyList<string> lines,
        int start,
        int end)
    {
        var fenceStart = -1;
        for (var index = start; index < end; index++)
        {
            if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenceStart = index + 1;
                break;
            }
        }

        if (fenceStart < 0)
        {
            return null;
        }

        var command = new List<string>();
        for (var index = fenceStart; index < end; index++)
        {
            if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                return string.Join('\n', command).Trim();
            }

            command.Add(lines[index]);
        }

        throw SchemaError("A repro command fence was not closed.");
    }

    private static ExternalTriageSummary? ParseExternalTriage(PerfIssueComment comment)
    {
        if (!comment.Body.Contains("Automated Triage Analysis", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var summaryMatch = TriageSummaryPattern().Match(comment.Body);
        if (!summaryMatch.Success)
        {
            return null;
        }

        var raw = summaryMatch.Groups["summary"].Value.Trim();
        var links = new List<SafeExternalLink>();
        foreach (Match match in PlainMarkdownLinkPattern().Matches(raw))
        {
            if (Uri.TryCreate(match.Groups["uri"].Value, UriKind.Absolute, out var uri)
                && SafeChangeSetLinks.IsTriage(uri))
            {
                links.Add(new SafeExternalLink(
                    WebUtility.HtmlDecode(match.Groups["text"].Value),
                    uri));
            }
        }

        var summary = PlainMarkdownLinkPattern().Replace(raw, match =>
            WebUtility.HtmlDecode(match.Groups["text"].Value));
        summary = HtmlTagPattern().Replace(summary, string.Empty);
        return new ExternalTriageSummary(
            comment.Author,
            comment.CreatedAt,
            WebUtility.HtmlDecode(summary),
            links);
    }

    private static int FindGroupEnd(IReadOnlyList<string> lines, int start)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (lines[index].Trim().Equals("---", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static string ParseRuntimeSha(string? value, string label)
    {
        var match = MarkdownLinkPattern().Match(value ?? string.Empty);
        var sha = match.Success ? match.Groups["text"].Value : string.Empty;
        var uri = match.Success
            ? ParseAbsoluteUri(match.Groups["uri"].Value, label)
            : null;
        if (!GitHubCompareLinkBuilder.IsValidSha(sha)
            || uri is null
            || !SafeChangeSetLinks.IsRuntimeCommit(uri, sha))
        {
            throw SchemaError($"{label} runtime identity was invalid.");
        }

        return sha;
    }

    private static Uri ParseLink(string? value, string label)
    {
        var match = MarkdownLinkPattern().Match(value ?? string.Empty);
        return match.Success
            ? ParseAbsoluteUri(match.Groups["uri"].Value, label)
            : throw SchemaError($"{label} link was missing.");
    }

    private static Uri ParseAbsoluteUri(string value, string label) =>
        Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var uri)
            ? uri
            : throw SchemaError($"The {label} link was invalid.");

    private static string Required(
        IReadOnlyDictionary<string, string> values,
        string key) =>
        values.TryGetValue(key, out var value) && value.Length > 0
            ? WebUtility.HtmlDecode(value)
            : throw SchemaError($"Run information '{key}' was missing.");

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new JsonException($"'{property}' was missing.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

    private static PerfIssueException SchemaError(string detail) =>
        new(PerfIssueError.Schema, $"Could not parse the autofiling issue: {detail}");

    [GeneratedRegex(
        @"<!--\s*DATA:\s*(?<json>\{.*?\})\s*-->",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex DataPattern();

    [GeneratedRegex(
        @"^### (?<direction>Improvements|Regressions) in (?<name>.+)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex GroupHeadingPattern();

    [GeneratedRegex(
        @"\[(?<text>[^\]]+)\]\((?:<(?<uri>https://[^>]+)>|(?<uri>https://[^)\s]+))\)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex MarkdownLinkPattern();

    [GeneratedRegex(
        @"^\[Test Report\]\(<(?<uri>https://[^>]+)>\)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReportPattern();

    [GeneratedRegex(
        @"\[(?<text>[^\]]+)\]\((?<uri>https://[^)\s]+)\)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlainMarkdownLinkPattern();

    [GeneratedRegex(
        @"^\*\*Summary\*\*:\s*(?<summary>.+)$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TriageSummaryPattern();

    [GeneratedRegex(
        @"<[^>]+>",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlTagPattern();
}
