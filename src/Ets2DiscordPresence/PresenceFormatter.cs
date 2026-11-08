using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record PresenceText(string Details, string State, DateTime? Started = null, string? Image = null, string? Badge = null);
internal sealed record MapCity(string Name, double X, double Z, string? Id);

internal sealed class CityLookup
{
    private readonly List<MapCity> cities;
    public IReadOnlyList<MapCity> Cities => cities;
    public MapCity? FindDestination(string name, string? id) =>
        (string.IsNullOrWhiteSpace(id) ? null : cities.Find(city => string.Equals(city.Id, id, StringComparison.OrdinalIgnoreCase)))
        ?? cities.Find(city => CultureInfo.CurrentCulture.CompareInfo.Compare(city.Name, name,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0);

    public CityLookup(Stream source)
    {
        cities = new();
        using var document = JsonDocument.Parse(source);
        foreach (var item in document.RootElement.GetProperty("citiesList").EnumerateArray())
        {
            var name = item.GetProperty("realName").GetString();
            if (!string.IsNullOrWhiteSpace(name) && TryCoordinate(item.GetProperty("x"), out var x)
                && TryCoordinate(item.GetProperty("z"), out var z))
                cities.Add(new MapCity(name.Trim(), x, z, item.TryGetProperty("gameName", out var id) ? id.GetString() : null));
        }
        if (cities.Count == 0 || cities.Count > 20_000) throw new InvalidDataException("Catálogo de ciudades vacío o demasiado grande.");
    }

    private static bool TryCoordinate(JsonElement value, out double number) =>
        double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number) && Math.Abs(number) <= 2_000_000;

    public string? FindNearby(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || cities.Count == 0) return null;
        MapCity? nearest = null;
        var distanceSquared = 8000.0 * 8000.0;
        foreach (var city in cities)
        {
            var dx = city.X - x;
            var dz = city.Z - z;
            var candidate = dx * dx + dz * dz;
            if (candidate >= distanceSquared) continue;
            distanceSquared = candidate;
            nearest = city;
        }
        return nearest?.Name;
    }
}

internal static class PresenceFormatter
{
    public static readonly string[] Fields = ["cargo", "route", "city", "distance"];

    public static string[] Order(AppSettings settings) =>
        (settings.PresenceOrder ?? (settings.RouteFirst ? ["route", "cargo", "city", "distance"] : Fields))
            .Where(Fields.Contains).Concat(Fields).Distinct().ToArray();

    public static PresenceText Format(TelemetrySnapshot data, string? city, AppSettings? settings = null)
    {
        settings ??= new AppSettings();
        var english = settings.DiscordLanguage == "en";
        string T(string es, string en) => english ? en : es;
        if (data.Paused) return new PresenceText(T("Juego en pausa", "Game paused"), "Euro Truck Simulator 2");
        var location = city is null || !settings.ShowCity ? null : T($"Cerca de {city}", $"Near {city}");
        var distance = settings.ShowDistance && data.RemainingKm is { } km
            ? T($"{Math.Ceiling(km)} km restantes", $"{Math.Ceiling(km)} km remaining") : null;
        var cargo = data.OnJob && settings.ShowCargo && !string.IsNullOrWhiteSpace(data.Cargo)
            ? T($"Entregando {data.Cargo}", $"Delivering {data.Cargo}") : null;
        var route = data.OnJob && settings.ShowRoute && !string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity)
            ? $"{data.SourceCity} → {data.DestinationCity}" : null;
        var fields = Order(settings).Select(key => key switch
        {
            "cargo" => cargo, "route" => route, "city" => location, "distance" => distance, _ => null
        }).OfType<string>().ToArray();
        if (!data.OnJob)
            return new PresenceText(T("Explorando libremente", "Free roaming"),
                Limit(fields.Length > 0 ? string.Join(" · ", fields) : T("Conduciendo por Europa", "Driving across Europe")));
        var details = fields.FirstOrDefault() ?? T("Entrega en curso", "Delivery in progress");
        return new PresenceText(Limit(details), Limit(fields.Length > 1 ? string.Join(" · ", fields.Skip(1))
            : "Euro Truck Simulator 2"));
    }

    private static string Limit(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= 128) return text;
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 125) break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.Append('…').ToString();
    }
}
