using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WasmBenchmarkHistory.Data;

public sealed class BenchmarkHistoryParser
{
    private static readonly Regex DefaultCounterPattern = new(
        @"\bvar\s+defaultCounter\s*=\s*new\s+CounterEntry\s*\(\s*(?<id>\d+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    public BenchmarkHistory Parse(string benchmark, RunConfiguration run, string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmark);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(html);

        var counterMatch = DefaultCounterPattern.Match(html);
        if (!counterMatch.Success)
        {
            throw SchemaError(benchmark, "defaultCounter was not found.");
        }

        var counterId = counterMatch.Groups["id"].Value;
        var assignmentPattern = new Regex(
            $@"\btrendData\s*\[\s*{Regex.Escape(counterId)}\s*\]\s*=",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
        var assignment = assignmentPattern.Match(html, counterMatch.Index + counterMatch.Length);
        if (!assignment.Success)
        {
            throw SchemaError(benchmark, $"trendData[{counterId}] was not found.");
        }

        var traceArrayStart = SkipWhitespace(html, assignment.Index + assignment.Length);
        if (traceArrayStart >= html.Length || html[traceArrayStart] != '[')
        {
            throw SchemaError(benchmark, "The primary trend trace array was malformed.");
        }

        var primaryStart = SkipWhitespace(html, traceArrayStart + 1);
        if (primaryStart >= html.Length || html[primaryStart] != '{')
        {
            throw SchemaError(benchmark, "The primary trend trace object was missing.");
        }

        var primary = ExtractDelimited(html, primaryStart, '{', '}', benchmark);
        var timestamps = ParseStringArray(ExtractArray(primary, "x", benchmark), benchmark, "x");
        var values = ParseRequiredNumberArray(ExtractArray(primary, "y", benchmark), benchmark, "y");
        var gitHash = ExtractObject(primary, "gitHash", benchmark);
        var runtimeShas = ParseStringArray(
            ExtractArray(gitHash, "runtime", benchmark),
            benchmark,
            "gitHash.runtime");
        var performanceShas = ParseStringArray(
            ExtractArray(primary, "perfRepoHash", benchmark),
            benchmark,
            "perfRepoHash");
        var traceName = TryExtractString(primary, "name", benchmark);

        IReadOnlyList<double?> errors;
        var errorObject = TryExtractObject(primary, "error_y", benchmark);
        if (errorObject is null)
        {
            errors = Enumerable.Repeat<double?>(null, values.Count).ToArray();
        }
        else
        {
            errors = ParseNullableNumberArray(
                ExtractArray(errorObject, "array", benchmark),
                benchmark,
                "error_y.array");
        }

        var lengths = new[]
        {
            timestamps.Count,
            values.Count,
            runtimeShas.Count,
            performanceShas.Count,
            errors.Count
        };
        if (lengths.Any(length => length != values.Count))
        {
            throw SchemaError(
                benchmark,
                $"Primary trace fields are not aligned ({string.Join(", ", lengths)}).");
        }

        var observations = new BenchmarkObservation[values.Count];
        for (var index = 0; index < observations.Length; index++)
        {
            if (!DateTime.TryParseExact(
                    timestamps[index],
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var timestamp))
            {
                throw SchemaError(benchmark, $"Timestamp '{timestamps[index]}' was not recognized.");
            }

            observations[index] = new BenchmarkObservation(
                benchmark,
                run.Id,
                DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified),
                values[index],
                errors[index],
                runtimeShas[index],
                performanceShas[index],
                traceName);
        }

        return new BenchmarkHistory(benchmark, run, traceName, observations);
    }

    private static string ExtractArray(string source, string property, string benchmark)
    {
        var valueStart = FindPropertyValue(source, property, benchmark, required: true);
        if (source[valueStart] != '[')
        {
            throw SchemaError(benchmark, $"'{property}' was not an array.");
        }

        return ExtractDelimited(source, valueStart, '[', ']', benchmark);
    }

    private static string ExtractObject(string source, string property, string benchmark) =>
        TryExtractObject(source, property, benchmark)
        ?? throw SchemaError(benchmark, $"'{property}' was not found.");

    private static string? TryExtractObject(string source, string property, string benchmark)
    {
        var valueStart = FindPropertyValue(source, property, benchmark, required: false);
        if (valueStart < 0)
        {
            return null;
        }

        if (source[valueStart] != '{')
        {
            throw SchemaError(benchmark, $"'{property}' was not an object.");
        }

        return ExtractDelimited(source, valueStart, '{', '}', benchmark);
    }

    private static string? TryExtractString(string source, string property, string benchmark)
    {
        var valueStart = FindPropertyValue(source, property, benchmark, required: false);
        if (valueStart < 0)
        {
            return null;
        }

        if (source[valueStart] is not ('\'' or '"'))
        {
            throw SchemaError(benchmark, $"'{property}' was not a string.");
        }

        var position = valueStart;
        return ParseJavaScriptString(source, ref position, benchmark, property);
    }

    private static int FindPropertyValue(
        string source,
        string property,
        string benchmark,
        bool required)
    {
        var pattern = new Regex(
            $@"[""']{Regex.Escape(property)}[""']\s*:",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var match = pattern.Match(source);
        if (!match.Success)
        {
            if (required)
            {
                throw SchemaError(benchmark, $"'{property}' was not found.");
            }

            return -1;
        }

        var start = SkipWhitespace(source, match.Index + match.Length);
        if (start >= source.Length)
        {
            throw SchemaError(benchmark, $"'{property}' did not have a value.");
        }

        return start;
    }

    private static string ExtractDelimited(
        string source,
        int start,
        char opening,
        char closing,
        string benchmark)
    {
        var depth = 0;
        char? quote = null;
        var escaped = false;

        for (var index = start; index < source.Length; index++)
        {
            var character = source[index];
            if (quote is not null)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == opening)
            {
                depth++;
            }
            else if (character == closing && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw SchemaError(benchmark, $"Unterminated '{opening}' value.");
    }

    private static IReadOnlyList<string> ParseStringArray(
        string array,
        string benchmark,
        string field)
    {
        var values = new List<string>();
        var position = 1;

        while (true)
        {
            position = SkipWhitespaceAndCommas(array, position);
            if (position >= array.Length || array[position] == ']')
            {
                return values;
            }

            if (array[position] is not ('\'' or '"'))
            {
                throw SchemaError(benchmark, $"'{field}' contained a non-string value.");
            }

            values.Add(ParseJavaScriptString(array, ref position, benchmark, field));
        }
    }

    private static string ParseJavaScriptString(
        string source,
        ref int position,
        string benchmark,
        string field)
    {
        var quote = source[position++];
        var value = new StringBuilder();

        while (position < source.Length)
        {
            var character = source[position++];
            if (character == quote)
            {
                return value.ToString();
            }

            if (character != '\\')
            {
                value.Append(character);
                continue;
            }

            if (position >= source.Length)
            {
                break;
            }

            var escape = source[position++];
            value.Append(escape switch
            {
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'b' => '\b',
                'f' => '\f',
                'u' => ParseHexEscape(source, ref position, 4, benchmark, field),
                'x' => ParseHexEscape(source, ref position, 2, benchmark, field),
                _ => escape
            });
        }

        throw SchemaError(benchmark, $"'{field}' contained an unterminated string.");
    }

    private static char ParseHexEscape(
        string source,
        ref int position,
        int digits,
        string benchmark,
        string field)
    {
        if (position + digits > source.Length
            || !int.TryParse(
                source.AsSpan(position, digits),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw SchemaError(benchmark, $"'{field}' contained an invalid hexadecimal escape.");
        }

        position += digits;
        return (char)value;
    }

    private static IReadOnlyList<double> ParseRequiredNumberArray(
        string array,
        string benchmark,
        string field)
    {
        var nullableValues = ParseNullableNumberArray(array, benchmark, field);
        if (nullableValues.Any(value => value is null))
        {
            throw SchemaError(benchmark, $"'{field}' contained null.");
        }

        return nullableValues.Select(value => value!.Value).ToArray();
    }

    private static IReadOnlyList<double?> ParseNullableNumberArray(
        string array,
        string benchmark,
        string field)
    {
        var body = array.AsSpan(1, array.Length - 2);
        if (body.Trim().IsEmpty)
        {
            return [];
        }

        var values = new List<double?>();
        foreach (var segment in body.ToString().Split(','))
        {
            var token = segment.Trim();
            if (token.Equals("null", StringComparison.Ordinal))
            {
                values.Add(null);
                continue;
            }

            if (!double.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || !double.IsFinite(value))
            {
                throw SchemaError(benchmark, $"'{field}' contained invalid number '{token}'.");
            }

            values.Add(value);
        }

        return values;
    }

    private static int SkipWhitespace(string source, int position)
    {
        while (position < source.Length && char.IsWhiteSpace(source[position]))
        {
            position++;
        }

        return position;
    }

    private static int SkipWhitespaceAndCommas(string source, int position)
    {
        while (position < source.Length
               && (char.IsWhiteSpace(source[position]) || source[position] == ','))
        {
            position++;
        }

        return position;
    }

    private static BenchmarkDataException SchemaError(string benchmark, string detail) =>
        new(
            BenchmarkDataError.Schema,
            $"Could not parse history for '{benchmark}': {detail}");
}
