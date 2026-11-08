using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record RecordedFrame(long ElapsedMs, TelemetrySnapshot? Trip);

internal static class TelemetryReplay
{
    public static List<RecordedFrame> Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length is 0 or > 30_000_000) throw new InvalidDataException("Recording exceeds the allowed size.");
        using var input = new StreamReader(file);
        var frames = new List<RecordedFrame>();
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var frame = JsonSerializer.Deserialize<RecordedFrame>(line) ?? throw new InvalidDataException("Invalid frame.");
            if (frame.ElapsedMs < 0 || frame.ElapsedMs > 7 * 24 * 60 * 60 * 1000L
                || (frames.Count > 0 && frames[^1].ElapsedMs > frame.ElapsedMs) || frames.Count >= 35_000)
                throw new InvalidDataException("Invalid recording timeline.");
            frames.Add(frame);
        }
        if (frames.Count == 0) throw new InvalidDataException("The recording is empty.");
        return frames;
    }

    public static int IndexAt(IReadOnlyList<RecordedFrame> frames, long elapsed)
    {
        var lower = 0;
        var upper = frames.Count;
        while (lower < upper)
        {
            var middle = (lower + upper) / 2;
            if (frames[middle].ElapsedMs <= elapsed) lower = middle + 1;
            else upper = middle;
        }
        return lower - 1;
    }

    public static IEnumerable<(long Elapsed, string Kind)> Milestones(IReadOnlyList<RecordedFrame> frames)
    {
        TelemetrySnapshot? previous = null;
        foreach (var frame in frames)
        {
            if (frame.Trip is { Signals: { } signals } trip)
            {
                var old = previous?.Signals;
                if (trip.OnJob && signals.Loaded && (previous?.OnJob != true || old?.Loaded != true))
                    yield return (frame.ElapsedMs, "pickup");
                if (old is not null && signals.Ferry != old.Ferry)
                    yield return (frame.ElapsedMs, "ferry");
                if (old is not null && signals.FinishedAt != 0
                    && (signals.FinishedAt != old.FinishedAt || signals.Delivered != old.Delivered)
                    && signals.Delivered != old.Delivered)
                    yield return (frame.ElapsedMs, "delivery");
            }
            previous = frame.Trip;
        }
    }

    public static void Write(TextWriter file, RecordedFrame frame)
    {
        file.WriteLine(JsonSerializer.Serialize(frame));
        file.Flush();
    }
}

internal sealed class PresencePublication
{
    private PresenceText? last;
    private int previousGeneration;

    public bool ShouldSend(PresenceText? presence, int generation)
    {
        if (presence == last && (presence is null || generation == previousGeneration)) return false;
        last = presence;
        previousGeneration = generation;
        return true;
    }

    public static PresenceText? Select(PresenceText? presence, bool simulated, bool sharing, bool connected) =>
        !simulated && sharing && connected ? presence : null;
}
