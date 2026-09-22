namespace WasmBenchmarkHistory.Data;

public enum HistoryRangeKind
{
    ThirtyDays,
    NinetyDays,
    OneYear,
    All,
    Custom
}

public sealed record HistoryTimeRange(
    HistoryRangeKind Kind,
    DateTime? Start = null,
    DateTime? End = null)
{
    public static HistoryTimeRange ThirtyDays { get; } = new(HistoryRangeKind.ThirtyDays);

    public static HistoryTimeRange NinetyDays { get; } = new(HistoryRangeKind.NinetyDays);

    public static HistoryTimeRange OneYear { get; } = new(HistoryRangeKind.OneYear);

    public static HistoryTimeRange All { get; } = new(HistoryRangeKind.All);

    public static HistoryTimeRange Custom(DateTime start, DateTime end)
    {
        if (start >= end)
        {
            throw new ArgumentException("A custom history range must end after it starts.");
        }

        return new(HistoryRangeKind.Custom, start, end);
    }

    public (DateTime Start, DateTime End) Resolve(DateTime minimum, DateTime maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentException("The available history range is invalid.");
        }

        var requestedStart = Kind switch
        {
            HistoryRangeKind.ThirtyDays => maximum.AddDays(-30),
            HistoryRangeKind.NinetyDays => maximum.AddDays(-90),
            HistoryRangeKind.OneYear => maximum.AddYears(-1),
            HistoryRangeKind.All => minimum,
            HistoryRangeKind.Custom when Start is not null && End is not null => Start.Value,
            _ => minimum
        };
        var requestedEnd = Kind == HistoryRangeKind.Custom && End is not null
            ? End.Value
            : maximum;

        var resolvedStart = requestedStart < minimum ? minimum : requestedStart;
        var resolvedEnd = requestedEnd > maximum ? maximum : requestedEnd;
        return resolvedStart < resolvedEnd
            ? (resolvedStart, resolvedEnd)
            : (minimum, maximum);
    }

    public bool Contains(DateTime timestamp, DateTime minimum, DateTime maximum)
    {
        var range = Resolve(minimum, maximum);
        return timestamp >= range.Start && timestamp <= range.End;
    }
}
