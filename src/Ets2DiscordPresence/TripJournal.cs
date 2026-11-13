using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record TripEntry(Guid Id, string Key, string Origin, string Destination, string Cargo,
    DateTimeOffset Started, DateTimeOffset? Finished, string Result, double Km, double DrivingSeconds, string Notes = "", string Tags = "", bool Favorite = false, string? Truck = null, double FuelLitres = 0, string? Recording = null,
    DateTimeOffset? DeletedAt = null, bool? OnTime = null, double FuelKm = 0, int? Stops = null, TripMeasurements? Measurements = null, string? JourneyType = null, long? Revenue = null);

internal sealed record TripMeasurements(string Quality, DateTimeOffset TrackedFrom, double ObservedKm = 0,
    double MovingSeconds = 0, double StoppedSeconds = 0, double SpeedSeconds = 0, double? MaximumKph = null,
    double RefueledLitres = 0, double? DeliveredKm = null, string[]? Countries = null, string? DestinationCountry = null,
    Dictionary<string, double>? CountryKm = null, DateTimeOffset? PickedUpAt = null)
{
    public double? AverageKph => MovingSeconds > 0 ? SpeedSeconds / MovingSeconds : null;
}
internal sealed record JournalData(List<TripEntry> Entries, List<TripEntry> Pending, List<TripEntry>? Trash = null);

internal sealed class TripJournal
{
    public static string DataFolder => LocalData.Folder;
    private readonly string path;
    private readonly JournalData data;
    private TripEntry? active;
    private TelemetrySnapshot? previous;
    private long previousAt;
    private string? previousCountry;
    private long lastSaved;
    private long lastFerry;
    private long lastRest;
    private bool dirty;
    private long? stoppedSince;
    private bool stopCounted;

    public IReadOnlyList<TripEntry> Entries => data.Entries;
    public IReadOnlyList<TripEntry> Trash => data.Trash!;
    public string? Error { get; private set; }
    public bool Recovered { get; private set; }
    public TripMeasurements? LiveMeasurements => active?.Measurements;
    public string? BoundaryCountry { get; private set; }

    public TripJournal(string? path = null)
    {
        this.path = path ?? Path.Combine(DataFolder, "trips.json");
        data = new([], [], []);
        try
        {
            if (File.Exists(this.path))
            {
                var saved = JsonSerializer.Deserialize<JournalData>(File.ReadAllText(this.path));
                if (saved?.Entries is null || saved.Pending is null) throw new JsonException();
                data = saved with { Trash = saved.Trash ?? [] };
                data.Trash!.RemoveAll(entry => entry.DeletedAt < DateTimeOffset.UtcNow.AddDays(-30));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "journal.read";
        }
    }

    private TripEntry? drive;
    private TelemetrySnapshot? previousDrive;
    private long previousDriveAt;
    private long lastDrivingAt;

    public TripEntry? ObserveDrive(TelemetrySnapshot? trip, DateTimeOffset utc, long now, bool keepFreeDrive, bool gameRunning)
    {
        var kind = trip is { Coach: true, OnJob: true, DestinationCity.Length: > 0 } ? "coach"
            : trip is { OnJob: false } && keepFreeDrive ? "free" : null;
        var key = kind == "coach" ? $"coach|{trip!.SourceCity}|{trip.DestinationCity}|{trip.Signals?.DestinationId}" : kind;
        TripEntry? closed = null;
        if (drive is { } activeDrive && (!gameRunning || trip is not null && key != activeDrive.Key
            || activeDrive.JourneyType == "free" && now - lastDrivingAt >= 300_000))
        {
            if (activeDrive.Km >= .1 || activeDrive.DrivingSeconds >= 30)
            {
                closed = activeDrive with { Finished = utc, Result = "ended" };
                data.Entries.Add(closed);
                dirty = true;
                Flush(now);
            }
            drive = null;
            previousDrive = null;
        }

        if (trip is { Paused: false, Signals: { } current } && kind is not null)
        {
            if (drive is null && Math.Abs(current.Speed) > .25f)
            {
                drive = new TripEntry(Guid.NewGuid(), key!, trip.SourceCity, trip.DestinationCity, "",
                    utc, null, "", 0, 0, Truck: TruckLabel(trip.Truck), JourneyType: kind);
                lastDrivingAt = now;
            }
            if (drive is not null && previousDrive is { Signals: { } prior } previousTrip)
            {
                var delta = current.Odometer - prior.Odometer;
                var elapsed = now - previousDriveAt;
                if (ValidInterval(previousTrip, trip, delta, elapsed) && Math.Abs(current.Speed) > .25f)
                {
                    lastDrivingAt = now;
                    drive = drive with { Km = drive.Km + delta, DrivingSeconds = drive.DrivingSeconds + elapsed / 1000d };
                }
            }
            previousDrive = drive is null ? null : trip;
            previousDriveAt = now;
        }
        else previousDrive = null;
        return closed;
    }

    public TripEntry? Observe(TelemetrySnapshot? trip, DateTimeOffset utc, long now, string? country = null)
    {
        BoundaryCountry = null;
        if (trip?.Signals is not { } signals)
        {
            if (active?.Measurements is { Quality: "complete" } measurements)
            {
                active = active with { Measurements = measurements with { Quality = "partial" } };
                SaveActive();
            }
            previous = null;
            previousCountry = null;
            stoppedSince = null;
            stopCounted = false;
            lastFerry = lastRest = 0;
            return null;
        }

        var old = previous?.Signals;
        var finished = active is not null && old is not null
            && signals.FinishedAt != 0 && (signals.FinishedAt != old.FinishedAt
                || signals.Delivered != old.Delivered || signals.Cancelled != old.Cancelled);
        TripEntry? summary = null;
        if (finished && active is { } ended)
        {
            var delivered = signals.Delivered != old!.Delivered;
            var cancelled = signals.Cancelled != old.Cancelled;
            if (delivered != cancelled)
            {
                var km = delivered && signals.DeliveredKm is > 0 and < 100_000 ? signals.DeliveredKm : ended.Km;
                summary = ended with
                {
                    Finished = utc, Result = delivered ? "delivered" : "cancelled", Km = km,
                    Revenue = delivered ? signals.Revenue : null,
                    Measurements = ended.Measurements is { } measured ? measured with
                    {
                        DeliveredKm = delivered && signals.DeliveredKm is > 0 and < 100_000 ? signals.DeliveredKm : null,
                        DestinationCountry = delivered ? country : null
                    } : null,
                    OnTime = delivered && old.Deadline > 0 ? signals.FinishedAt <= old.Deadline : null
                };
                data.Entries.Add(summary);
                data.Pending.RemoveAll(item => item.Id == ended.Id);
                active = null;
                dirty = true;
                Flush(now);
            }
        }

        if (old is not null && signals.Ferry != old.Ferry) lastFerry = now + 20_000;
        if (old is not null && signals.RestMinutes > old.RestMinutes + 120
            && trip.GameMinutes > previous!.GameMinutes && trip.GameMinutes - previous.GameMinutes >= 120
            && Math.Abs(old.Speed) < .25f && Math.Abs(signals.Speed) < .25f
            && now - previousAt is > 0 and <= 6000 && lastFerry <= now)
            lastRest = now + 15_000;

        if (trip.OnJob && signals.Deadline > 0 && !string.IsNullOrWhiteSpace(trip.DestinationCity))
        {
            var key = $"{signals.Deadline}|{signals.JobKey}|{trip.SourceCity}|{trip.DestinationCity}|{trip.Cargo}";
            var sameTrip = active?.Key == key;
            if (!sameTrip)
            {
                stoppedSince = null;
                stopCounted = false;
                active = data.Pending.LastOrDefault(item => item.Key == key);
                Recovered = active is not null;
                active ??= new TripEntry(Guid.NewGuid(), key, trip.SourceCity, trip.DestinationCity, trip.Cargo,
                    utc, null, "", 0, 0, Truck: TruckLabel(trip.Truck), Stops: 0);
                var quality = Recovered ? "recovered" : previous is { OnJob: false } ? "complete" : "partial";
                var measurements = active.Measurements ?? new TripMeasurements(quality, utc);
                active = active with { Measurements = measurements with { Quality = quality } };
                SaveActive();
                Flush(now);
            }
            if (signals.Loaded && active?.Measurements is { PickedUpAt: null } pickup)
            {
                active = active with { Measurements = pickup with { PickedUpAt = utc } };
                SaveActive();
            }
            if (signals.Loaded && !trip.Paused && country is not null && active?.Measurements is { } observed
                && observed.Countries?.LastOrDefault() != country)
            {
                if (observed.Countries is { Length: > 0 }) BoundaryCountry = country;
                active = active with { Measurements = observed with { Countries = [.. observed.Countries ?? [], country] } };
                SaveActive();
            }
            if (sameTrip && old is { Loaded: true } && signals.Loaded && previous is { Paused: false, OnJob: true } && !trip.Paused)
            {
                var delta = signals.Odometer - old.Odometer;
                var elapsed = now - previousAt;
                var valid = ValidInterval(previous, trip, delta, elapsed);
                if (valid && Math.Abs(signals.Speed) <= .25f)
                {
                    stoppedSince ??= now;
                    if (!stopCounted && now - stoppedSince >= 10_000)
                    {
                        stopCounted = true;
                        active = active! with { Stops = (active.Stops ?? 0) + 1 };
                        SaveActive();
                    }
                }
                else
                {
                    stoppedSince = null;
                    stopCounted = false;
                }
                if (active?.Measurements is { } measured && valid)
                {
                    var seconds = elapsed / 1000d;
                    var speed = Math.Abs(signals.Speed) * 3.6;
                    var moving = Math.Abs(signals.Speed) > .25f;
                    var sameTruck = TruckLabel(previous.Truck) == TruckLabel(trip.Truck)
                        && previous.Truck?.Capacity == trip.Truck?.Capacity;
                    var litres = sameTruck && previous.Truck?.FuelPercent is not null && trip.Truck?.FuelPercent is not null
                        ? previous.Truck.Litres - trip.Truck.Litres : null;
                    var used = litres is >= 0 and <= 2;
                    var added = litres is < 0 && (old.Refueling || signals.Refueling);
                    var countryKm = measured.CountryKm;
                    if (delta > 0 && country is not null && country == previousCountry)
                    {
                        countryKm ??= new();
                        countryKm[country] = countryKm.GetValueOrDefault(country) + delta;
                    }
                    active = active with
                    {
                        Km = active.Km + delta,
                        DrivingSeconds = active.DrivingSeconds + (moving ? seconds : 0),
                        FuelLitres = active.FuelLitres + (used ? litres!.Value : 0),
                        FuelKm = active.FuelKm + (used || added ? delta : 0),
                        Measurements = measured with
                        {
                            ObservedKm = measured.ObservedKm + delta,
                            MovingSeconds = measured.MovingSeconds + (moving ? seconds : 0),
                            StoppedSeconds = measured.StoppedSeconds + (moving ? 0 : seconds),
                            SpeedSeconds = measured.SpeedSeconds + (moving ? speed * seconds : 0),
                            MaximumKph = Math.Max(measured.MaximumKph ?? 0, speed),
                            RefueledLitres = measured.RefueledLitres + (added ? -litres!.Value : 0),
                            CountryKm = countryKm
                        }
                    };
                    SaveActive();
                }
            }
        }
        else if (!trip.OnJob)
        {
            active = null;
            Recovered = false;
        }

        if (trip.Paused || !trip.OnJob)
        {
            stoppedSince = null;
            stopCounted = false;
        }
        previous = trip;
        previousCountry = country;
        previousAt = now;
        if (now - lastSaved >= 15_000) Flush(now);
        return summary;
    }

    private static bool ValidInterval(TelemetrySnapshot previous, TelemetrySnapshot current, double delta, long elapsed) =>
        elapsed is > 0 and <= 6000 && delta is >= 0 and <= .6
        && previous.Signals is { Ferry: false, Speed: >= -100 and <= 100 }
        && current.Signals is { Ferry: false, Speed: >= -100 and <= 100 }
        && (previous.RenderTime != current.RenderTime || current.RenderTime == 0)
        && double.IsFinite(current.X) && double.IsFinite(current.Z)
        && Math.Abs(current.X - previous.X) + Math.Abs(current.Z - previous.Z) <= 1500;

    public string? Manoeuvre(TelemetrySnapshot trip, long now)
    {
        if (trip.Signals is not { } signals || trip.Paused) return null;
        if (lastFerry > now) return "ferry";
        if (lastRest > now) return "rest";
        if (signals.Refueling) return "fuel";
        if (signals.ParkBrake && Math.Abs(signals.Speed) < .25f) return "parked";
        if (signals.Speed < -.25f) return "reversing";
        return null;
    }

    public bool UpdateDetails(Guid id, string notes, string tags, bool favorite)
    {
        var index = data.Entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return false;
        var previous = data.Entries[index];
        data.Entries[index] = previous with { Notes = notes, Tags = tags, Favorite = favorite };
        dirty = true;
        Flush();
        if (Error is null) return true;
        data.Entries[index] = previous;
        return false;
    }

    public bool Remove(Guid id)
    {
        var index = data.Entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return false;
        var previous = data.Entries[index];
        data.Entries.RemoveAt(index);
        data.Trash!.Add(previous with { DeletedAt = DateTimeOffset.UtcNow });
        dirty = true;
        Flush();
        if (Error is null) return true;
        data.Trash.RemoveAt(data.Trash.Count - 1);
        data.Entries.Insert(index, previous);
        return false;
    }

    public bool Restore(Guid id)
    {
        var entry = data.Trash!.Find(item => item.Id == id);
        if (entry is null || data.Entries.Any(item => item.Id == id)) return false;
        data.Trash.Remove(entry);
        data.Entries.Add(entry with { DeletedAt = null });
        dirty = true;
        Flush();
        if (Error is null) return true;
        data.Entries.RemoveAt(data.Entries.Count - 1);
        data.Trash.Add(entry);
        return false;
    }

    public void AttachRecording(Guid id, string recording)
    {
        var index = data.Entries.FindIndex(entry => entry.Id == id);
        if (index < 0) return;
        data.Entries[index] = data.Entries[index] with { Recording = recording };
        dirty = true;
        Flush();
    }

    public bool EditMany(IEnumerable<Guid> ids, string? tag, bool favorite)
    {
        var selected = ids.ToHashSet();
        var previous = data.Entries.ToArray();
        for (var index = 0; index < data.Entries.Count; index++)
        {
            var entry = data.Entries[index];
            if (!selected.Contains(entry.Id)) continue;
            var tags = tag is null ? entry.Tags : string.Join(", ", JournalQuery.Tags(entry.Tags).Append(tag)
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
            data.Entries[index] = entry with { Tags = tags, Favorite = favorite || entry.Favorite };
        }
        dirty = true;
        Flush();
        if (Error is null) return true;
        data.Entries.Clear();
        data.Entries.AddRange(previous);
        return false;
    }

    public bool Move(Guid id, TripJournal target) => MoveMany([id], target);

    public bool MoveMany(IEnumerable<Guid> ids, TripJournal target)
    {
        if (target.Error is not null || Error is not null || string.Equals(path, target.path, StringComparison.OrdinalIgnoreCase)) return false;
        var selected = ids.ToHashSet();
        var before = data.Entries.ToArray();
        var entries = before.Where(entry => selected.Contains(entry.Id)).ToArray();
        if (entries.Length == 0) return false;
        var added = entries.Where(entry => target.data.Entries.All(other => other.Id != entry.Id)).ToArray();
        if (added.Length > 0)
        {
            target.data.Entries.AddRange(added);
            target.dirty = true;
            target.Flush();
            if (target.Error is not null)
            {
                target.data.Entries.RemoveAll(added.Contains);
                return false;
            }
        }
        data.Entries.RemoveAll(entry => selected.Contains(entry.Id));
        dirty = true;
        Flush();
        if (Error is null) return true;
        data.Entries.Clear();
        data.Entries.AddRange(before);
        if (added.Length > 0)
        {
            target.data.Entries.RemoveAll(added.Contains);
            target.dirty = true;
            target.Flush();
        }
        return false;
    }

    private static string? TruckLabel(TruckInfo? truck)
    {
        if (truck is null) return null;
        var label = $"{truck.Brand} {truck.Model}".Trim();
        return label.Length == 0 ? null : label;
    }

    public void Flush(long now = 0)
    {
        lastSaved = now;
        if (!dirty || Error == "journal.read") return;
        try
        {
            LocalData.WriteFile(path, JsonSerializer.Serialize(data));
            dirty = false;
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = "journal.write"; }
    }

    private void SaveActive()
    {
        var trip = active!;
        data.Pending.RemoveAll(item => item.Id == trip.Id);
        data.Pending.Add(trip);
        if (data.Pending.Count > 32) data.Pending.RemoveAt(0);
        dirty = true;
    }

    public static void Export(string file, IEnumerable<TripEntry> entries, bool english)
    {
        var csv = new StringBuilder(english
            ? "Finished,Origin,Destination,Cargo,Result,Distance km,Driving minutes,Trip type,Revenue\r\n"
            : "Fecha,Origen,Destino,Carga,Resultado,Distancia km,Minutos al volante,Tipo de viaje,Ingresos\r\n");
        foreach (var entry in entries)
        {
            var result = entry.Result switch
            {
                "delivered" => english ? "Delivered" : "Entregada",
                "ended" => english ? "Session ended" : "Sesión cerrada",
                _ => english ? "Cancelled" : "Cancelada"
            };
            var kind = entry.JourneyType switch
            {
                "coach" => english ? "Passengers" : "Pasajeros",
                "free" => english ? "Free driving" : "Conducción libre",
                _ => english ? "Delivery" : "Entrega"
            };
            csv.AppendLine(string.Join(',', Csv(entry.Finished?.ToLocalTime().ToString("O") ?? ""),
                Csv(entry.Origin), Csv(entry.Destination), Csv(entry.Cargo), Csv(result),
                entry.Km.ToString("0.0", CultureInfo.InvariantCulture),
                (entry.DrivingSeconds / 60).ToString("0.0", CultureInfo.InvariantCulture), Csv(kind),
                entry.Revenue?.ToString(CultureInfo.InvariantCulture) ?? ""));
        }
        File.WriteAllText(file, csv.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') ||
            value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') ||
            value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
