using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record TripEntry(Guid Id, string Key, string Origin, string Destination, string Cargo,
    DateTimeOffset Started, DateTimeOffset? Finished, string Result, double Km, double DrivingSeconds, string Notes = "", string Tags = "", bool Favorite = false);
internal sealed record JournalData(List<TripEntry> Entries, List<TripEntry> Pending);

internal sealed class TripJournal
{
    public static string DataFolder => LocalData.Folder;
    private readonly string path;
    private readonly JournalData data;
    private TripEntry? active;
    private TelemetrySnapshot? previous;
    private long previousAt;
    private long lastSaved;
    private long lastFerry;
    private long lastRest;
    private bool dirty;

    public IReadOnlyList<TripEntry> Entries => data.Entries;
    public string? Error { get; private set; }
    public bool Recovered { get; private set; }

    public TripJournal(string? path = null)
    {
        this.path = path ?? Path.Combine(DataFolder, "trips.json");
        data = new([], []);
        try
        {
            if (File.Exists(this.path))
            {
                var saved = JsonSerializer.Deserialize<JournalData>(File.ReadAllText(this.path));
                if (saved?.Entries is null || saved.Pending is null) throw new JsonException();
                data = saved;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "journal.read";
        }
    }

    public TripEntry? Observe(TelemetrySnapshot? trip, DateTimeOffset utc, long now)
    {
        if (trip?.Signals is not { } signals)
        {
            previous = null;
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
                summary = ended with { Finished = utc, Result = delivered ? "delivered" : "cancelled", Km = km };
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
                active = data.Pending.LastOrDefault(item => item.Key == key);
                Recovered = active is not null;
                active ??= new TripEntry(Guid.NewGuid(), key, trip.SourceCity, trip.DestinationCity, trip.Cargo,
                    utc, null, "", 0, 0);
                SaveActive();
                Flush(now);
            }
            if (sameTrip && old is not null && previous is { Paused: false, OnJob: true } && !trip.Paused)
            {
                var delta = signals.Odometer - old.Odometer;
                var elapsed = now - previousAt;
                if (elapsed is > 0 and <= 6000 && Math.Abs(signals.Speed) > .25f && delta is >= 0 and <= 10)
                {
                    active = active! with { Km = active.Km + delta, DrivingSeconds = active.DrivingSeconds + elapsed / 1000d };
                    SaveActive();
                }
            }
        }
        else if (!trip.OnJob)
        {
            active = null;
            Recovered = false;
        }

        previous = trip;
        previousAt = now;
        if (now - lastSaved >= 15_000) Flush(now);
        return summary;
    }

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
        dirty = true;
        Flush();
        if (Error is null) return true;
        data.Entries.Insert(index, previous);
        return false;
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
            ? "Finished,Origin,Destination,Cargo,Result,Distance km,Driving minutes\r\n"
            : "Fecha,Origen,Destino,Carga,Resultado,Distancia km,Minutos al volante\r\n");
        foreach (var entry in entries)
        {
            var result = entry.Result == "delivered" ? (english ? "Delivered" : "Entregada") : (english ? "Cancelled" : "Cancelada");
            csv.AppendLine(string.Join(',', Csv(entry.Finished?.ToLocalTime().ToString("O") ?? ""),
                Csv(entry.Origin), Csv(entry.Destination), Csv(entry.Cargo), Csv(result),
                entry.Km.ToString("0.0", CultureInfo.InvariantCulture),
                (entry.DrivingSeconds / 60).ToString("0.0", CultureInfo.InvariantCulture)));
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
