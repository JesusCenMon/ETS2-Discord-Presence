using System.Text.Json;
using System.IO.Compression;

namespace Ets2DiscordPresence;

internal sealed record RecordedFrame(long ElapsedMs, TelemetrySnapshot? Trip, string? Marker = null, string? Event = null, string? Note = null);

internal static class TelemetryReplay
{
    internal const int MaximumFrames = 35_000;
    internal const long MaximumBytes = 30_000_000;

    internal sealed record RecordingMetadata(int SchemaVersion, string AppVersion, DateTimeOffset Created,
        DateTimeOffset? StartedAt = null, long? Duration = null, string? Origin = null, string? Destination = null,
        string? Truck = null, int? FrameCount = null, int? MarkersCount = null, string[]? MarkerTitles = null, string? JourneyType = null);
    internal sealed record RecordingRead(List<RecordedFrame> Frames, bool RecoveredTail, RecordingMetadata? Metadata);

    public static List<RecordedFrame> Read(string path) => ReadDetailed(path).Frames;

    public static RecordingRead ReadDetailed(string path)
    {
        using var file = File.OpenRead(path);
        return ReadFile(file, path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
    }

    internal static RecordingRead ReadFile(Stream file, bool compressed)
    {
        if (file.Length is 0 or > MaximumBytes) throw new InvalidDataException("Recording exceeds the allowed size.");
        if (!compressed) return Parse(file);
        using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer)) != 0)
        {
            if (content.Length + count > MaximumBytes) throw new InvalidDataException("Recording exceeds the allowed size.");
            content.Write(buffer, 0, count);
        }
        if (content.Length == 0) throw new InvalidDataException("The recording is empty.");
        return Parse(content);
    }

    public static RecordingMetadata? ReadHeader(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file;
        using var input = new StreamReader(stream);
        var header = new char[8192];
        var count = 0;
        while (count < header.Length && input.Read() is var character && character >= 0 && character != '\n')
            header[count++] = (char)character;
        if (count == 0 || count == header.Length) return null;
        using var parsed = JsonDocument.Parse(new string(header, 0, count));
        return parsed.RootElement.TryGetProperty("SchemaVersion", out _)
            ? parsed.RootElement.Deserialize<RecordingMetadata>() : null;
    }

    private static RecordingRead Parse(Stream file)
    {
        file.Seek(-1, SeekOrigin.End);
        var incompleteTail = file.ReadByte() != '\n';
        file.Seek(0, SeekOrigin.Begin);
        using var input = new StreamReader(file);
        var frames = new List<RecordedFrame>();
        RecordingMetadata? metadata = null;
        var recoveredTail = false;
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            RecordedFrame frame;
            try
            {
                using var document = JsonDocument.Parse(line);
                var item = document.RootElement;
                if (item.TryGetProperty("SchemaVersion", out _))
                {
                    if (metadata is not null || frames.Count > 0
                        || item.Deserialize<RecordingMetadata>() is not { SchemaVersion: 1 or 2 } header
                        || !Version.TryParse(header.AppVersion, out _) || header.Created == default)
                        throw new InvalidDataException("Unsupported recording schema.");
                    metadata = header;
                    continue;
                }
                if (!item.TryGetProperty("ElapsedMs", out _)) throw new InvalidDataException("Invalid frame.");
                frame = item.Deserialize<RecordedFrame>() ?? throw new InvalidDataException("Invalid frame.");
            }
            catch (JsonException) when (incompleteTail && input.EndOfStream && frames.Count > 0)
            {
                recoveredTail = true;
                break;
            }
            if (frame.ElapsedMs < 0 || frame.ElapsedMs > 7 * 24 * 60 * 60 * 1000L
                || (frames.Count > 0 && frames[^1].ElapsedMs > frame.ElapsedMs) || frames.Count >= MaximumFrames)
                throw new InvalidDataException("Invalid recording timeline.");
            frames.Add(frame);
        }
        if (frames.Count == 0) throw new InvalidDataException("The recording is empty.");
        return new RecordingRead(frames, recoveredTail, metadata);
    }

    public static void WriteHeader(TextWriter writer) =>
        writer.WriteLine(JsonSerializer.Serialize(new RecordingMetadata(1, DesktopServices.Version.ToString(3), DateTimeOffset.UtcNow)));

    internal static TelemetrySnapshot? Interpolate(IReadOnlyList<RecordedFrame> frames, int index, long elapsed)
    {
        if (index < 0) return null;
        var current = frames[index];
        if (current.Trip is not { Paused: false } earlier || index + 1 >= frames.Count
            || frames[index + 1] is not { Trip: { Paused: false } to } next
            || next.ElapsedMs - current.ElapsedMs is <= 0 or > 6000 || earlier.Signals?.Ferry != to.Signals?.Ferry
            || Math.Abs(to.X - earlier.X) + Math.Abs(to.Z - earlier.Z) > 4000)
            return current.Trip;
        var progress = Math.Clamp((double)(elapsed - current.ElapsedMs) / (next.ElapsedMs - current.ElapsedMs), 0, 1);
        return earlier with { X = earlier.X + (to.X - earlier.X) * progress, Z = earlier.Z + (to.Z - earlier.Z) * progress };
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
            if (!string.IsNullOrEmpty(frame.Event)) yield return (frame.ElapsedMs, frame.Event);
            foreach (var kind in DetectEvents(frame.Trip, previous)) yield return (frame.ElapsedMs, kind);
            previous = frame.Trip;
        }
    }

    internal static IEnumerable<string> DetectEvents(TelemetrySnapshot? trip, TelemetrySnapshot? previous)
    {
        if (trip?.Signals is not { } signals) yield break;
        var old = previous?.Signals;
        if (trip.OnJob && signals.Loaded && (previous?.OnJob != true || old?.Loaded != true)) yield return "pickup";
        if (old is null) yield break;
        if (signals.Refueling && !old.Refueling) yield return "fuel";
        if (signals.RestMinutes > old.RestMinutes + 120 && Math.Abs(signals.Speed) < .25
            && previous is not null && (long)trip.GameMinutes - previous.GameMinutes >= 120 && signals.Ferry == old.Ferry)
            yield return "rest";
        if (signals.Ferry != old.Ferry) yield return "ferry";
        if (signals.FinishedAt != 0 && signals.Delivered != old.Delivered) yield return "delivery";
    }

    public static void Save(string path, IEnumerable<RecordedFrame> frames, DateTimeOffset? startedAt = null)
    {
        var samples = frames as IReadOnlyList<RecordedFrame> ?? frames.ToArray();
        if (samples.Count is 0 or > MaximumFrames) throw new InvalidDataException("Invalid recording length.");
        var trip = samples.Select(frame => frame.Trip).FirstOrDefault(sample => sample?.OnJob == true);
        var truck = samples.Select(frame => frame.Trip?.Truck).FirstOrDefault(value => value is not null);
        var kind = trip is not null ? (trip.Coach ? "coach" : "delivery")
            : samples.Any(frame => frame.Trip is not null) ? "free" : null;
        var metadata = new RecordingMetadata(2, DesktopServices.Version.ToString(3), DateTimeOffset.UtcNow,
            startedAt, samples[^1].ElapsedMs, trip?.SourceCity, trip?.DestinationCity,
            truck is null ? null : $"{truck.Brand} {truck.Model}".Trim(), samples.Count,
            samples.Count(frame => !string.IsNullOrWhiteSpace(frame.Marker)),
            samples.Select(frame => frame.Marker).OfType<string>().Where(title => title.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(24).Select(title => title[..Math.Min(title.Length, 50)]).ToArray(), kind);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew))
            using (var writer = new StreamWriter(path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(output, CompressionLevel.Optimal) : output))
            {
                writer.WriteLine(JsonSerializer.Serialize(metadata));
                foreach (var frame in samples) writer.WriteLine(JsonSerializer.Serialize(frame));
            }
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
