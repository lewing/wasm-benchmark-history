using System.Globalization;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class DurationFormatterTests
{
    [Theory]
    [InlineData(.0318, "31.8\u00a0ps")]
    [InlineData(31.8, "31.8\u00a0ns")]
    [InlineData(2_530, "2.53\u00a0µs")]
    [InlineData(44_300_000, "44.3\u00a0ms")]
    [InlineData(1_270_000_000, "1.27\u00a0s")]
    [InlineData(0, "0\u00a0ns")]
    [InlineData(-2_530, "-2.53\u00a0µs")]
    public void Format_UsesAdaptiveUnits(double nanoseconds, string expected)
    {
        Assert.Equal(expected, DurationFormatter.Format(nanoseconds, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(.999, DurationUnit.Picoseconds)]
    [InlineData(1, DurationUnit.Nanoseconds)]
    [InlineData(999.999, DurationUnit.Nanoseconds)]
    [InlineData(1_000, DurationUnit.Microseconds)]
    [InlineData(1_000_000, DurationUnit.Milliseconds)]
    [InlineData(1_000_000_000, DurationUnit.Seconds)]
    public void SelectUnit_UsesExactThresholds(double nanoseconds, DurationUnit expected)
    {
        Assert.Equal(expected, DurationFormatter.SelectUnit(nanoseconds));
    }

    [Fact]
    public void AxisFormatting_UsesOneStableUnit()
    {
        var unit = DurationFormatter.SelectUnit(900, 1_100);

        Assert.Equal(DurationUnit.Microseconds, unit);
        Assert.Equal("0.900\u00a0µs",
            DurationFormatter.Format(900, unit, CultureInfo.InvariantCulture));
        Assert.Equal("1.10\u00a0µs",
            DurationFormatter.Format(1_100, unit, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ErrorAndVarianceCanUseTheMeanUnit()
    {
        var unit = DurationFormatter.SelectUnit(2_530);

        Assert.Equal("0.125\u00a0µs",
            DurationFormatter.Format(125, unit, CultureInfo.InvariantCulture));
        Assert.Equal("0.0156\u00a0µs²",
            DurationFormatter.FormatVariance(15_625, unit, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ExactNanosecondsAvoidBinaryArtifacts()
    {
        Assert.Equal("0.1\u00a0ns",
            DurationFormatter.FormatExactNanoseconds(.1, CultureInfo.InvariantCulture));
        Assert.Equal("2,530.125\u00a0ns",
            DurationFormatter.FormatExactNanoseconds(2530.125, CultureInfo.InvariantCulture));
        Assert.Equal("360,052.734375\u00a0ns",
            DurationFormatter.FormatExactNanoseconds(360052.73437500006, CultureInfo.InvariantCulture));
        Assert.Equal("15,625\u00a0ns²",
            DurationFormatter.FormatExactNanosecondsSquared(15_625, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormattingIsLocaleAware()
    {
        Assert.Equal("2,53\u00a0µs",
            DurationFormatter.Format(2_530, CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidValuesAreUnavailable(double value)
    {
        Assert.Equal("unavailable", DurationFormatter.Format(value, CultureInfo.InvariantCulture));
        Assert.Equal("unavailable", DurationFormatter.FormatExactNanoseconds(value));
    }
}
