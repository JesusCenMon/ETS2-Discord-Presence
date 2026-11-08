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
