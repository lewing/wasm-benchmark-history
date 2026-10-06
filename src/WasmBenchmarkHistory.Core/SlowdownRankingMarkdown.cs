using System.Globalization;
using System.Text;

namespace WasmBenchmarkHistory.Data;

public static class SlowdownRankingMarkdown
{
    public static string Create(
        BuildComparisonResult comparison,
        BenchmarkRankingResult ranking,
        string candidateId,
        string baselineId,
        string? filter = null)
    {
        var candidateName = LaneName(comparison, candidateId);
        var baselineName = LaneName(comparison, baselineId);
        var build = comparison.Snapshot.Build;
        var builder = new StringBuilder();

        builder.AppendLine("# Slowdown ranking");
        builder.AppendLine();
        builder.AppendLine($"- **Build:** {Escape(build.BuildId)} / {Escape(build.BuildNumber)} ({Escape(build.SourceDate)})");
        builder.AppendLine($"- **Measured configuration:** {Escape(candidateName)}");
        builder.AppendLine($"- **Reference configuration:** {Escape(baselineName)}");
        if (!string.IsNullOrWhiteSpace(filter))
            builder.AppendLine($"- **Filter:** {Escape(filter.Trim())}");
        builder.AppendLine($"- **Rows:** {Number(ranking.Rows.Length)} of {Number(ranking.SlowerCount)} slower benchmarks");
        builder.AppendLine($"- **Comparable benchmarks:** {Number(ranking.ComparableCount)}");
        builder.AppendLine($"- **Excluded benchmarks:** {Number(ranking.ExcludedCount)}");
        builder.AppendLine();
        builder.AppendLine("> Slowdown = (measured mean - reference mean) / reference mean x 100. " +
            "Rankings are descriptive and do not establish statistical significance.");
        builder.AppendLine();
        builder.AppendLine($"| # | Microbenchmark | {Escape(candidateName)} mean | {Escape(baselineName)} mean | Slower by |");
        builder.AppendLine("|---:|---|---:|---:|---:|");

        for (var index = 0; index < ranking.Rows.Length; index++)
        {
            var row = ranking.Rows[index];
            builder.AppendLine(
                $"| {index + 1} | {Escape(row.Identity.DisplayName)} | {Duration(row.CandidateMean)} | " +
                $"{Duration(row.BaselineMean)} | {Percent(row.SlowdownPercent)} |");
        }

        return builder.ToString();
    }

    public static string FileName(string buildId, string candidateId, string baselineId) =>
        $"slowdowns-{FilePart(buildId)}-{FilePart(candidateId)}-vs-{FilePart(baselineId)}.md";

    private static string LaneName(BuildComparisonResult comparison, string id) =>
        comparison.Snapshot.Lanes.FirstOrDefault(
            lane => lane.Provenance.Id.Equals(id, StringComparison.Ordinal))?.Provenance.DisplayName
        ?? throw new ArgumentException("Choose a known runtime configuration.", nameof(id));

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Duration(double value) =>
        DurationFormatter.FormatExactNanoseconds(value, CultureInfo.InvariantCulture)
            .Replace('\u00a0', ' ');

    private static string Percent(double value) =>
        "+" + value.ToString("N1", CultureInfo.InvariantCulture) + "%";

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\r' or '\n')
            {
                builder.Append(' ');
                continue;
            }
            if (character is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '|')
                builder.Append('\\');
            builder.Append(character);
        }
        return builder.ToString();
    }

    private static string FilePart(string value)
    {
        var characters = value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray();
        var part = new string(characters).Trim('-');
        return part.Length == 0 ? "unknown" : part;
    }
}
