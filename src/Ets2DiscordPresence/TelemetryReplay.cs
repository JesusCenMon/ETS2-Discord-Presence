using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record RecordedFrame(long ElapsedMs, TelemetrySnapshot? Trip, string? Marker = null);

internal static class TelemetryReplay
{
    internal const int MaximumFrames = 35_000;
    internal const long MaximumBytes = 30_000_000;

    public static List<RecordedFrame> Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length is 0 or > MaximumBytes) throw new InvalidDataException("Recording exceeds the allowed size.");
        file.Seek(-1, SeekOrigin.End);
        var incompleteTail = file.ReadByte() != '\n';
        file.Seek(0, SeekOrigin.Begin);
        using var input = new StreamReader(file);
        var frames = new List<RecordedFrame>();
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            RecordedFrame frame;
            try { frame = JsonSerializer.Deserialize<RecordedFrame>(line) ?? throw new InvalidDataException("Invalid frame."); }
            catch (JsonException) when (incompleteTail && input.EndOfStream && frames.Count > 0) { break; }
            if (frame.ElapsedMs < 0 || frame.ElapsedMs > 7 * 24 * 60 * 60 * 1000L
                || (frames.Count > 0 && frames[^1].ElapsedMs > frame.ElapsedMs) || frames.Count >= MaximumFrames)
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
            if (!string.IsNullOrWhiteSpace(frame.Marker)) yield return (frame.ElapsedMs, frame.Marker);
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

    public static void Save(string path, IEnumerable<RecordedFrame> frames)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temporary, FileMode.CreateNew)))
                foreach (var frame in frames) writer.WriteLine(JsonSerializer.Serialize(frame));
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public static List<RecordedFrame> Trim(IReadOnlyList<RecordedFrame> frames, long start, long end)
    {
        if (start < 0 || start >= end || end > frames[^1].ElapsedMs) throw new ArgumentOutOfRangeException(nameof(start));
        var output = frames.Where(frame => frame.ElapsedMs >= start && frame.ElapsedMs <= end)
            .Select(frame => frame with { ElapsedMs = frame.ElapsedMs - start }).ToList();
        if (output.Count == 0 || output[0].ElapsedMs != 0)
        {
            var index = IndexAt(frames, start);
            output.Insert(0, new RecordedFrame(0, index >= 0 ? frames[index].Trip : null));
        }
        if (output[^1].ElapsedMs < end - start) output.Add(new RecordedFrame(end - start, output[^1].Trip));
        return output;
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
