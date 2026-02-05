namespace TimeTrialPlugin.Timing;

public record LapTime
{
    public required string TrackId { get; init; }
    public required string PlayerName { get; init; }
    public required ulong PlayerGuid { get; init; }
    public required string CarModel { get; init; }
    public required long TotalTimeMs { get; init; }
    public required long[] SectorTimesMs { get; init; }
    public required DateTime RecordedAt { get; init; }

    public string FormattedTotalTime => FormatTime(TotalTimeMs);
    public string[] FormattedSectorTimes => SectorTimesMs.Select(FormatTime).ToArray();

    public static string FormatTime(long timeMs)
    {
        var ts = TimeSpan.FromMilliseconds(timeMs);
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}"
            : $"{ts.Seconds}.{ts.Milliseconds:D3}";
    }
}
