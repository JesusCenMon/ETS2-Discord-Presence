using System.Diagnostics;

namespace Ets2DiscordPresence;

// Bounded, in-memory measurements. Never records routes, player names or screenshots.
internal sealed class UiPerformance
{
    internal const int Capacity = 120;
    internal sealed record Sample(string Section, double HandlerMs, double? IdleMs, int BudgetMs,
        bool Cold, DateTimeOffset At);
    private readonly Queue<Sample> samples = new();
    private readonly Queue<double> stalls = new();
    private readonly HashSet<string> visited = [];
    internal IReadOnlyList<Sample> Samples => samples.ToArray();
    internal IReadOnlyList<double> Stalls => stalls.ToArray();
    internal bool Visit(string section) => visited.Add(section);
    internal Sample Record(string section, double handlerMs, double? idleMs, bool cold)
    {
        var sample = new Sample(section, handlerMs, idleMs, cold ? 500 : 100, cold, DateTimeOffset.UtcNow);
        samples.Enqueue(sample);
        while (samples.Count > Capacity) samples.Dequeue();
        return sample;
    }
    internal void Settle(Sample sample, double milliseconds)
    {
        var pending = samples.ToArray();
        samples.Clear();
        foreach (var item in pending)
            samples.Enqueue(ReferenceEquals(item, sample) ? item with { IdleMs = milliseconds } : item);
    }
    internal void RecordStall(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 150) return;
        stalls.Enqueue(milliseconds);
        while (stalls.Count > 32) stalls.Dequeue();
    }
    internal string Report() => string.Join(Environment.NewLine,
        samples.Select(sample => $"{sample.Section}: handler={sample.HandlerMs:F1} ms; "
            + $"idle={(sample.IdleMs is { } idle ? idle.ToString("F1") : "not measured")} ms; "
            + $"budget={sample.BudgetMs} ms; cold={sample.Cold}"))
        + Environment.NewLine + $"UI queue stalls >=150 ms: {stalls.Count}; max={stalls.DefaultIfEmpty().Max():F1} ms";
}

internal sealed class ScreenStability
{
    private GameScreen? candidate;
    private int matches;
    private int misses;
    private long lastMatch;
    internal GameScreen? Current { get; private set; }
    internal GameScreen? Observe(GameScreenReader.Scan scan, long now)
    {
        // Alt-tab and unavailable captures are not evidence that the player left a screen.
        if (scan.Status is GameScreenReader.ScanStatus.GameNotInForeground or GameScreenReader.ScanStatus.CaptureUnavailable)
            return now - lastMatch <= 6500 ? Current : Current = null;
        if (scan.Screen is { } screen)
        {
            matches = candidate == screen ? matches + 1 : 1;
            candidate = screen;
            misses = 0;
            if (matches >= 2) { Current = screen; lastMatch = now; }
        }
        else
        {
            candidate = null;
            matches = 0;
            if (++misses >= 2) Current = null;
        }
        return now - lastMatch <= 6500 ? Current : Current = null;
    }
    internal void Reset() { candidate = Current = null; matches = misses = 0; lastMatch = 0; }
}
