using System.Globalization;

namespace WasmBenchmarkHistory.Data;

public enum DurationUnit
{
    Picoseconds,
    Nanoseconds,
    Microseconds,
    Milliseconds,
    Seconds
}

public static class DurationFormatter
{
    private const string NonBreakingSpace = "\u00a0";

    public static DurationUnit SelectUnit(double nanoseconds) =>
        SelectUnit(nanoseconds, nanoseconds);

    public static DurationUnit SelectUnit(double minimumNanoseconds, double maximumNanoseconds)
    {
        var magnitude = Math.Max(Math.Abs(minimumNanoseconds), Math.Abs(maximumNanoseconds));
        if (!double.IsFinite(magnitude))
            return DurationUnit.Nanoseconds;
        return magnitude switch
        {
            > 0 and < 1 => DurationUnit.Picoseconds,
            < 1_000 => DurationUnit.Nanoseconds,
            < 1_000_000 => DurationUnit.Microseconds,
            < 1_000_000_000 => DurationUnit.Milliseconds,
            _ => DurationUnit.Seconds
        };
    }

    public static string Format(double? nanoseconds, CultureInfo? culture = null) =>
        nanoseconds is { } value
            ? Format(value, SelectUnit(value), culture)
            : "unavailable";

    public static string Format(
        double? nanoseconds,
        DurationUnit unit,
        CultureInfo? culture = null) =>
        nanoseconds is { } value ? Format(value, unit, culture) : "unavailable";

    public static string Format(
        double nanoseconds,
        DurationUnit unit,
        CultureInfo? culture = null,
        bool includeUnit = true)
    {
        if (!double.IsFinite(nanoseconds))
            return "unavailable";
        culture ??= CultureInfo.CurrentCulture;
        var scaled = nanoseconds / NanosecondsPer(unit);
        var formatted = scaled == 0
            ? "0"
            : scaled.ToString($"N{DecimalPlaces(Math.Abs(scaled))}", culture);
        return includeUnit ? formatted + NonBreakingSpace + Symbol(unit) : formatted;
    }

    public static string FormatVariance(
        double? nanosecondsSquared,
        DurationUnit unit,
        CultureInfo? culture = null)
    {
        if (nanosecondsSquared is not { } value || !double.IsFinite(value))
            return "unavailable";
        culture ??= CultureInfo.CurrentCulture;
        var factor = NanosecondsPer(unit);
        var scaled = value / (factor * factor);
        var formatted = scaled == 0
            ? "0"
            : scaled.ToString($"N{DecimalPlaces(Math.Abs(scaled))}", culture);
        return formatted + NonBreakingSpace + Symbol(unit) + "²";
    }

    public static string FormatExactNanoseconds(double? nanoseconds, CultureInfo? culture = null)
    {
        if (nanoseconds is not { } value || !double.IsFinite(value))
            return "unavailable";
        culture ??= CultureInfo.CurrentCulture;
        var absolute = Math.Abs(value);
        var number = absolute is > 0 and < 1e-10
            ? value.ToString("G12", culture)
            : value.ToString(ExactPattern(absolute), culture);
        return number + NonBreakingSpace + "ns";
    }

    public static string FormatExactNanosecondsSquared(
        double? nanosecondsSquared,
        CultureInfo? culture = null)
    {
        var formatted = FormatExactNanoseconds(nanosecondsSquared, culture);
        return formatted == "unavailable" ? formatted : formatted[..^2] + "ns²";
    }

    public static string Symbol(DurationUnit unit) => unit switch
    {
        DurationUnit.Picoseconds => "ps",
        DurationUnit.Nanoseconds => "ns",
        DurationUnit.Microseconds => "µs",
        DurationUnit.Milliseconds => "ms",
        DurationUnit.Seconds => "s",
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    private static double NanosecondsPer(DurationUnit unit) => unit switch
    {
        DurationUnit.Picoseconds => .001,
        DurationUnit.Nanoseconds => 1,
        DurationUnit.Microseconds => 1_000,
        DurationUnit.Milliseconds => 1_000_000,
        DurationUnit.Seconds => 1_000_000_000,
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    private static int DecimalPlaces(double magnitude)
    {
        if (magnitude >= 100)
            return 0;
        if (magnitude >= 10)
            return 1;
        if (magnitude >= 1)
            return 2;
        if (magnitude == 0)
            return 0;
        return Math.Clamp(2 - (int)Math.Floor(Math.Log10(magnitude)), 3, 6);
    }

    private static string ExactPattern(double magnitude)
    {
        if (magnitude == 0)
            return "0";
        var digitsBeforeDecimal = magnitude >= 1
            ? (int)Math.Floor(Math.Log10(magnitude)) + 1
            : 0;
        var decimals = Math.Clamp(12 - digitsBeforeDecimal, 0, 15);
        return decimals == 0 ? "#,0" : "#,0." + new string('#', decimals);
    }
}
