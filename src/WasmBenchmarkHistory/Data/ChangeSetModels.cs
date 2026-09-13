using System.Globalization;

namespace WasmBenchmarkHistory.Data;

public sealed record PerfIssueReference(int Number)
{
    public const string Owner = "dotnet";
    public const string Repository = "perf-autofiling-issues";

    public Uri ApiUri =>
        new($"https://api.github.com/repos/{Owner}/{Repository}/issues/{Number}");

    public Uri CommentsApiUri =>
        new($"https://api.github.com/repos/{Owner}/{Repository}/issues/{Number}/comments?per_page=100");

    public Uri HtmlUri =>
        new($"https://github.com/{Owner}/{Repository}/issues/{Number}");

    public static PerfIssueReference Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw InvalidInput();
        }

        var value = input.Trim();

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return Create(number);
        }

        var hash = value.LastIndexOf('#');
        if (hash > 0 && !value.Contains("://", StringComparison.Ordinal))
        {
            ValidateRepository(value[..hash]);
            return ParseNumber(value[(hash + 1)..]);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw InvalidInput();
        }

        var segments = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 4
            || !segments[2].Equals("issues", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidInput();
        }

        ValidateRepository($"{segments[0]}/{segments[1]}");
        return ParseNumber(segments[3]);
    }

    private static PerfIssueReference ParseNumber(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? Create(number)
            : throw InvalidInput();

    private static PerfIssueReference Create(int number) =>
        number > 0 ? new(number) : throw InvalidInput();

    private static void ValidateRepository(string repository)
    {
        if (!repository.Equals(
                $"{Owner}/{Repository}",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PerfIssueException(
                PerfIssueError.UnsupportedRepository,
                $"Only {Owner}/{Repository} issues are supported.");
        }
    }

    private static PerfIssueException InvalidInput() =>
        new(
            PerfIssueError.InvalidInput,
            $"Enter an issue number, {Owner}/{Repository}#number, or a public GitHub issue URL.");
}

public enum PerfIssueError
{
    InvalidInput,
    UnsupportedRepository,
    NotFound,
    RateLimited,
    Network,
    Schema
}

public sealed class PerfIssueException(
    PerfIssueError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public PerfIssueError Error { get; } = error;
}

public sealed record PerfIssuePayload(
    int Number,
    string Title,
    string State,
    Uri HtmlUri,
    string Body,
    IReadOnlyList<string> Labels,
    IReadOnlyList<PerfIssueComment> Comments);

public sealed record PerfIssueComment(
    string Author,
    DateTimeOffset CreatedAt,
    string Body);

public sealed record ChangeSetDocument(
    int IssueNumber,
    string Title,
    string State,
    Uri IssueUri,
    IReadOnlyList<string> Labels,
    ChangeSetMetadata Metadata,
    IReadOnlyList<ChangeSetGroup> Groups,
    ExternalTriageSummary? ExternalTriage)
{
    public int BenchmarkCount => Groups.Sum(group => group.Rows.Count);
}

public sealed record ChangeSetMetadata(
    string Repository,
    string Branch,
    string Architecture,
    string OperatingSystem,
    string Queue,
    string Frequency,
    IReadOnlyList<string> Configurations,
    DateTime? RegressionDate,
    bool IsRegression,
    string? PerformanceSha);

public enum ChangeDirection
{
    Improvement,
    Regression
}

public sealed record ChangeSetRunInformation(
    string Architecture,
    string OperatingSystem,
    string Queue,
    string BaselineRuntimeSha,
    string CompareRuntimeSha,
    Uri RuntimeDiffUri,
    string Configurations);

public sealed record ChangeSetGroup(
    string Name,
    ChangeDirection Direction,
    ChangeSetRunInformation Run,
    IReadOnlyList<ChangeSetBenchmark> Rows,
    string? ReproCommand);

public sealed record ChangeSetBenchmark(
    string Benchmark,
    string DisplayName,
    string ReportedBaseline,
    string ReportedTest,
    double Ratio,
    double? TestQuality,
    bool? EdgeDetector,
    Uri HistoryUri,
    Uri? SourceUri,
    Uri? ReportUri)
{
    public double PercentDelta => (Ratio - 1) * 100;
}

public enum ReportedSignalQuality
{
    StrongReportedSignal,
    Review,
    HighVarianceOrNoiseRisk
}

public static class ChangeSetSignalHeuristic
{
    public const double StrongMinimumMagnitudePercent = 10;
    public const double StrongMaximumReportedQuality = .05;
    public const double HighVarianceReportedQuality = .15;

    public static ReportedSignalQuality Classify(ChangeSetBenchmark benchmark)
    {
        var magnitude = Math.Abs(benchmark.PercentDelta);
        if (benchmark.EdgeDetector is true
            || benchmark.TestQuality is >= HighVarianceReportedQuality)
        {
            return ReportedSignalQuality.HighVarianceOrNoiseRisk;
        }

        if (magnitude >= StrongMinimumMagnitudePercent
            && benchmark.TestQuality is <= StrongMaximumReportedQuality
            && benchmark.EdgeDetector is false)
        {
            return ReportedSignalQuality.StrongReportedSignal;
        }

        return ReportedSignalQuality.Review;
    }

    public static string DisplayName(ReportedSignalQuality quality) => quality switch
    {
        ReportedSignalQuality.StrongReportedSignal => "strong reported signal",
        ReportedSignalQuality.HighVarianceOrNoiseRisk => "high variance/noise risk",
        _ => "review"
    };
}

public sealed record ExternalTriageSummary(
    string Author,
    DateTimeOffset CreatedAt,
    string Summary,
    IReadOnlyList<SafeExternalLink> Links);

public sealed record SafeExternalLink(string Text, Uri Uri);

public sealed record ImportedHistoryTarget(
    RunConfiguration Run,
    string Benchmark,
    Uri HistoryUri)
{
    public static ImportedHistoryTarget Parse(Uri historyUri)
    {
        if (!SafeChangeSetLinks.IsHistory(historyUri))
        {
            throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                "The imported history URL is outside the supported public report host.");
        }

        foreach (var run in KnownRunConfigurations.All)
        {
            var directory = SafeChangeSetLinks.DecodedDirectory(run.IndexUri);
            var path = Uri.UnescapeDataString(historyUri.AbsolutePath);
            if (!path.StartsWith(directory, StringComparison.Ordinal)
                || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var benchmark = path[directory.Length..^".html".Length];
            if (benchmark.Length == 0
                || benchmark.Equals("AllTestindex", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            return new ImportedHistoryTarget(run, benchmark, historyUri);
        }

        throw new BenchmarkDataException(
            BenchmarkDataError.UnknownBenchmark,
            "The imported history URL does not map exactly to a known report run.");
    }
}

public static class SafeChangeSetLinks
{
    private const string ReportHost = "pvscmdupload.z22.web.core.windows.net";

    public static bool IsHistory(Uri uri) =>
        IsHttpsHost(uri, ReportHost)
        && Uri.UnescapeDataString(uri.AbsolutePath)
            .StartsWith("/reports/allTestHistory/", StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsReport(Uri uri) =>
        IsHttpsHost(uri, ReportHost)
        && Uri.UnescapeDataString(uri.AbsolutePath)
            .StartsWith("/autofilereport/autofilereports/", StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsSource(Uri uri) =>
        IsHttpsHost(uri, "github.com")
        && (uri.AbsolutePath.StartsWith("/dotnet/performance/blob/", StringComparison.Ordinal)
            || uri.AbsolutePath.StartsWith("/dotnet/performance/tree/", StringComparison.Ordinal))
        && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsRuntimeCommit(Uri uri, string sha) =>
        IsHttpsHost(uri, "github.com")
        && uri.AbsolutePath.Equals(
            $"/dotnet/runtime/commit/{sha}",
            StringComparison.OrdinalIgnoreCase)
        && HasNoExtraUriParts(uri);

    public static bool IsRuntimeCompare(Uri uri, string baselineSha, string compareSha) =>
        IsHttpsHost(uri, "github.com")
        && uri.AbsolutePath.Equals(
            $"/dotnet/runtime/compare/{baselineSha}...{compareSha}",
            StringComparison.OrdinalIgnoreCase)
        && HasNoExtraUriParts(uri);

    public static bool IsTriage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("redirect.github.com", StringComparison.OrdinalIgnoreCase))
        && string.IsNullOrEmpty(uri.UserInfo);

    internal static string DecodedDirectory(Uri uri)
    {
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        return path[..(path.LastIndexOf('/') + 1)];
    }

    private static bool IsHttpsHost(Uri uri, string host) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase);

    private static bool HasNoExtraUriParts(Uri uri) =>
        string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
