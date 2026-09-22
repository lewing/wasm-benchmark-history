using System.Net;
using System.Text.RegularExpressions;

namespace WasmBenchmarkHistory.Data;

public sealed class BenchmarkIndexParser
{
    private static readonly Regex AnchorPattern = new(
        """<a\b[^>]*\bhref\s*=\s*(?<quote>["'])(?<href>.*?)\k<quote>[^>]*>.*?</a\s*>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    public IReadOnlyList<BenchmarkLink> Parse(Uri indexUri, string html)
    {
        ArgumentNullException.ThrowIfNull(indexUri);
        ArgumentNullException.ThrowIfNull(html);

        var decodedDirectory = GetDecodedDirectory(indexUri);
        var links = new Dictionary<string, BenchmarkLink>(StringComparer.Ordinal);

        foreach (Match match in AnchorPattern.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!Uri.TryCreate(indexUri, href, out var pageUri)
                || pageUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(pageUri.Host, indexUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var decodedPath = Uri.UnescapeDataString(pageUri.AbsolutePath);
            if (!decodedPath.StartsWith(decodedDirectory, StringComparison.Ordinal)
                || !decodedPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = decodedPath[decodedDirectory.Length..];
            if (relativePath.Equals("AllTestindex.html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var benchmark = relativePath[..^".html".Length];
            links.TryAdd(benchmark, new BenchmarkLink(benchmark, pageUri));
        }

        if (links.Count == 0)
        {
            throw new BenchmarkDataException(
                BenchmarkDataError.Schema,
                $"The index at {indexUri} did not contain any recognized benchmark links.");
        }

        return links.Values.OrderBy(link => link.Benchmark, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetDecodedDirectory(Uri indexUri)
    {
        var path = Uri.UnescapeDataString(indexUri.AbsolutePath);
        var finalSlash = path.LastIndexOf('/');
        return path[..(finalSlash + 1)];
    }
}
