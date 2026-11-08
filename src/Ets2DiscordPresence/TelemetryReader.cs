using System.IO.MemoryMappedFiles;
using System.Text;

namespace Ets2DiscordPresence;

internal sealed record TelemetrySnapshot(
    bool Paused, bool OnJob, string Cargo, string SourceCity, string DestinationCity,
    double X, double Z, float RouteDistanceMeters, uint PluginRevision,
    ulong RenderTime = 0, uint GameMinutes = 0, float RouteSeconds = 0, uint PlannedDistanceKm = 0,
    TripSignals? Signals = null, TruckInfo? Truck = null)
{
    public double? RemainingKm => float.IsFinite(RouteDistanceMeters) && RouteDistanceMeters > 0
        ? RouteDistanceMeters / 1000d : null;

    public int? Progress => OnJob && PlannedDistanceKm > 0 && RemainingKm is { } km && km <= PlannedDistanceKm
        ? Math.Clamp((int)Math.Floor(100 * (1 - km / PlannedDistanceKm)), 0, 99) : null;

    public string? ArrivalTime => RemainingKm.HasValue && float.IsFinite(RouteSeconds)
        && RouteSeconds is > 0 and < 604800
        ? TimeSpan.FromMinutes((GameMinutes + Math.Ceiling(RouteSeconds / 60d)) % 1440).ToString(@"hh\:mm")
        : null;
}

internal sealed record TruckInfo(string Brand, string Model, float? Litres, float? Capacity, float? RangeKm = null, float? FuelPerKm = null)
{
    public double? FuelPercent => Capacity is > 0 && Litres is >= 0 && Litres <= Capacity
        ? 100d * Litres.Value / Capacity.Value : null;
}

internal sealed record TripSignals(uint Deadline, uint FinishedAt, bool Delivered, bool Cancelled,
    bool Loaded, bool Ferry, bool Refueling, bool ParkBrake, float Speed, float Odometer,
    int RestMinutes, float DeliveredKm, string JobKey, string DestinationId = "");

internal static class TelemetryReader
{
    private const string MapName = @"Local\SCSTelemetry";
    private const int MinimumBytes = 4400;
    internal const uint ExpectedRevision = 12; // RenCloud scs-sdk-plugin V.1.12.1

    public static bool TryRead(out TelemetrySnapshot? snapshot, out string? error, out uint? detectedRevision)
    {
        detectedRevision = null;
        snapshot = null;
        error = null;
        try
        {
            using var map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, MinimumBytes, MemoryMappedFileAccess.Read);
            var bytes = new byte[MinimumBytes];
            // The plugin can update the map while it is read. Retry if its timestamp changes.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var before = view.ReadUInt64(24);
                view.ReadArray(0, bytes, 0, bytes.Length);
                var after = view.ReadUInt64(24);
                if (before == after || attempt == 2)
                {
                    if (bytes[0] != 0) detectedRevision = BitConverter.ToUInt32(bytes.AsSpan(40, 4));
                    snapshot = Parse(bytes);
                    break;
                }
            }
            if (snapshot is null) error = "ETS2 todavía no está enviando datos del viaje.";
            return snapshot is not null;
        }
        catch (FileNotFoundException)
        {
            error = "Esperando datos de ETS2.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            error = $"No se pudo leer ETS2: {ex.Message}";
            return false;
        }
    }

    // Offsets are from RenCloud's MIT-licensed scs-telemetry-common.hpp, tag V.1.12.1.
    // Rejecting a different revision avoids showing incorrect data if its layout changes.
    internal static TelemetrySnapshot? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumBytes || data[0] == 0) return null;
        var revision = BitConverter.ToUInt32(data.Slice(40, 4));
        if (revision != ExpectedRevision)
            throw new InvalidDataException($"Versión de telemetría no compatible ({revision}); se necesita la 12.");
        var game = BitConverter.ToUInt32(data.Slice(52, 4));
        if (game != 1) return null; // 1 = ETS2, 2 = ATS
        return new TelemetrySnapshot(
            Paused: data[4] != 0,
            OnJob: data[4300] != 0,
            Cargo: ReadString(data, 2620, 64),
            SourceCity: ReadString(data, 3004, 64),
            DestinationCity: ReadString(data, 2748, 64),
            X: BitConverter.ToDouble(data.Slice(2200, 8)),
            Z: BitConverter.ToDouble(data.Slice(2216, 8)),
            RouteDistanceMeters: BitConverter.ToSingle(data.Slice(1060, 4)),
            PluginRevision: revision,
            RenderTime: BitConverter.ToUInt64(data.Slice(24, 8)),
            GameMinutes: BitConverter.ToUInt32(data.Slice(64, 4)),
            RouteSeconds: BitConverter.ToSingle(data.Slice(1064, 4)),
            PlannedDistanceKm: BitConverter.ToUInt32(data.Slice(100, 4)),
            Signals: new TripSignals(
                Deadline: BitConverter.ToUInt32(data.Slice(88, 4)),
                FinishedAt: BitConverter.ToUInt32(data.Slice(448, 4)),
                Delivered: data[4303] != 0, Cancelled: data[4302] != 0,
                Loaded: data[1564] != 0, Ferry: data[4306] != 0, Refueling: data[4308] != 0,
                ParkBrake: data[1566] != 0,
                Speed: BitConverter.ToSingle(data.Slice(948, 4)),
                Odometer: BitConverter.ToSingle(data.Slice(1056, 4)),
                RestMinutes: BitConverter.ToInt32(data.Slice(500, 4)),
                DeliveredKm: BitConverter.ToSingle(data.Slice(1460, 4)),
                JobKey: string.Join("|", ReadString(data, 2556, 64), ReadString(data, 2940, 64),
                    ReadString(data, 3068, 64), ReadString(data, 2684, 64), ReadString(data, 2812, 64)),
                DestinationId: ReadString(data, 2684, 64)),
            Truck: new TruckInfo(ReadString(data, 2364, 64), ReadString(data, 2492, 64),
                FiniteFloat(data, 1000), FiniteFloat(data, 704), FiniteFloat(data, 1008), FiniteFloat(data, 1004)));
    }

    private static float? FiniteFloat(ReadOnlySpan<byte> data, int offset)
    {
        var value = BitConverter.ToSingle(data.Slice(offset, 4));
        return float.IsFinite(value) ? value : null;
    }

    private static string ReadString(ReadOnlySpan<byte> data, int offset, int length)
    {
        var slice = data.Slice(offset, length);
        var zero = slice.IndexOf((byte)0);
        return Encoding.UTF8.GetString(zero < 0 ? slice : slice[..zero]).Trim();
    }
}
