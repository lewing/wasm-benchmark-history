using System.Globalization;

namespace WasmBenchmarkHistory.Data;

public static class ChangeSetReportAge
{
    public const double ExpiringSoonDays = 4;

    public static string? Warning(DateTime? regressionDate, DateTimeOffset now)
    {
        if (regressionDate is null || regressionDate.Value == DateTime.MinValue)
        {
            return null;
        }

        var runUtc = DateTime.SpecifyKind(regressionDate.Value, DateTimeKind.Utc);
        var ageDays = Math.Max(0, (now.UtcDateTime - runUtc).TotalDays);
        var ageText = ageDays.ToString("0.0", CultureInfo.InvariantCulture);

        return ageDays >= ExpiringSoonDays
            ? $"The lab run was {ageText} days ago. Helix keeps BDN reports for about 5–7 days, "
                + "so they may be expiring or gone; fetch them now and use the KQL fallback if unavailable."
            : $"The lab run was {ageText} days ago. Fetch BDN reports now; Helix keeps them for about 5–7 days.";
    }
}
