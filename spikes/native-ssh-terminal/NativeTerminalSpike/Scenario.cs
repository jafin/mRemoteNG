using System.Text.Json.Nodes;

namespace NativeTerminalSpike;

/// <summary>One measured run: a command, the bytes it produced, and how the pipe coped.</summary>
public sealed class Scenario(string name, string sentinel)
{
    public string Name { get; } = name;
    public string Sentinel { get; } = sentinel;

    public double StartMs { get; set; }
    public double? FirstByteMs { get; set; }
    public double? LastByteMs { get; set; }
    public double? SentinelSeenMs { get; set; }
    public double? LastAckMs { get; set; }
    public double CompletedMs { get; set; }
    public bool TimedOut { get; set; }

    public long Bytes { get; set; }
    public long Chars { get; set; }
    public long RenderedBytes { get; set; }
    public int Replacements { get; set; }

    /// <summary>Per-chunk time from "posted to the page" to "xterm says it is on screen".</summary>
    public List<double> Lags { get; } = [];

    /// <summary>Gaps between successive renders — the worst one is the worst visible hitch.</summary>
    public List<double> Gaps { get; } = [];

    public double WallMs => CompletedMs - StartMs;

    public double RenderMBps => WallMs > 0 ? Bytes / 1024.0 / 1024.0 / (WallMs / 1000.0) : 0;

    public double ReadMBps
    {
        get
        {
            if (FirstByteMs is null || LastByteMs is null) return 0;
            double span = LastByteMs.Value - FirstByteMs.Value;
            return span > 0 ? Bytes / 1024.0 / 1024.0 / (span / 1000.0) : 0;
        }
    }

    public double MaxLagMs => Lags.Count > 0 ? Lags.Max() : 0;
    public double MaxGapMs => Gaps.Count > 0 ? Gaps.Max() : 0;

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return 0;
        List<double> sorted = [.. values];
        sorted.Sort();
        int index = (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["timedOut"] = TimedOut,
        ["bytes"] = Bytes,
        ["chars"] = Chars,
        ["renderedBytes"] = RenderedBytes,
        ["replacementChars"] = Replacements,
        ["chunks"] = Lags.Count,
        ["wallMs"] = Math.Round(WallMs, 1),
        ["renderMBps"] = Math.Round(RenderMBps, 3),
        ["readMBps"] = Math.Round(ReadMBps, 3),
        ["lagMs"] = new JsonObject
        {
            ["p50"] = Math.Round(Percentile(Lags, 50), 1),
            ["p95"] = Math.Round(Percentile(Lags, 95), 1),
            ["max"] = Math.Round(MaxLagMs, 1)
        },
        ["renderGapMs"] = new JsonObject
        {
            ["p50"] = Math.Round(Percentile(Gaps, 50), 1),
            ["p95"] = Math.Round(Percentile(Gaps, 95), 1),
            ["max"] = Math.Round(MaxGapMs, 1)
        }
    };
}
