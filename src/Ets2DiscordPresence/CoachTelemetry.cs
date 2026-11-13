using System.IO.MemoryMappedFiles;
using System.Text;

namespace Ets2DiscordPresence;

internal sealed record CoachJob(string Origin, string Destination, string DestinationId, uint PlannedKm);

internal static class CoachTelemetry
{
    private const string Mapping = @"Local\ETS2DiscordPresence.Coaches";
    private const int Size = 304;

    public static CoachJob? Read()
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(Mapping, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
            var data = new byte[Size];
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var sequence = view.ReadUInt32(0);
                if ((sequence & 1) != 0) continue;
                view.ReadArray(0, data, 0, data.Length);
                if (sequence == BitConverter.ToUInt32(data, 0) && sequence == view.ReadUInt32(0))
                    return Parse(data);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    internal static CoachJob? Parse(byte[] data)
    {
        if (data.Length != Size || BitConverter.ToUInt32(data, 4) != 1 || BitConverter.ToUInt32(data, 8) != 1)
            return null;
        return new(ReadText(data, 16), ReadText(data, 112), ReadText(data, 208), BitConverter.ToUInt32(data, 12));
    }

    private static string ReadText(byte[] data, int offset)
    {
        var bytes = data.AsSpan(offset, 96);
        var end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes).Trim();
    }

    internal static TelemetrySnapshot Apply(TelemetrySnapshot snapshot, CoachJob? job)
    {
        var knownCoach = IsLaunchCoach(snapshot.Truck);
        if (job is null && !knownCoach) return snapshot;
        var usableRoute = knownCoach && snapshot.OnJob && string.IsNullOrWhiteSpace(snapshot.Cargo);
        var onRoute = job is not null || usableRoute;
        return snapshot with
        {
            Coach = true,
            OnJob = onRoute,
            Cargo = "",
            SourceCity = Prefer(job?.Origin, usableRoute ? snapshot.SourceCity : ""),
            DestinationCity = Prefer(job?.Destination, usableRoute ? snapshot.DestinationCity : ""),
            PlannedDistanceKm = job is { PlannedKm: > 0 and < 100_000 } ? job.PlannedKm : usableRoute ? snapshot.PlannedDistanceKm : 0,
            Signals = snapshot.Signals is { } signals
                ? signals with { DestinationId = Prefer(job?.DestinationId, usableRoute ? signals.DestinationId : "") }
                : null
        };
    }

    private static string Prefer(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool IsLaunchCoach(TruckInfo? vehicle)
    {
        if (vehicle is null) return false;
        var model = string.Concat(vehicle.Model.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return model.StartsWith("scaniatouring", StringComparison.Ordinal)
            || model.StartsWith("volvo9700", StringComparison.Ordinal)
            || model.StartsWith("manlionscoach", StringComparison.Ordinal)
            || (vehicle.Brand.ToLowerInvariant() switch
            {
                "scania" => model.StartsWith("touring", StringComparison.Ordinal),
                "volvo" => model.StartsWith("9700", StringComparison.Ordinal),
                "man" => model.StartsWith("lionscoach", StringComparison.Ordinal),
                _ => false
            });
    }
}
