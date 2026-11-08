using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record PresenceText(string Details, string State);

internal sealed class CityLookup
{
    private readonly List<City> cities;
    private sealed record City(string Name, double X, double Z);

    public CityLookup(Stream source)
    {
        cities = new();
        using var document = JsonDocument.Parse(source);
        foreach (var item in document.RootElement.GetProperty("citiesList").EnumerateArray())
        {
            if (double.TryParse(item.GetProperty("x").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(item.GetProperty("z").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                cities.Add(new City(item.GetProperty("realName").GetString() ?? "", x, z));
        }
    }

    public string? FindNearby(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || cities.Count == 0) return null;
        City? nearest = null;
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
    public static PresenceText Format(TelemetrySnapshot data, string? city, AppSettings? settings = null)
    {
        settings ??= new AppSettings();
        if (data.Paused) return new PresenceText("Juego en pausa", "Euro Truck Simulator 2");
        var location = city is null || !settings.ShowCity ? null : $"Cerca de {city}";
        var distance = settings.ShowDistance && data.RemainingKm is { } km
            ? $"{Math.Ceiling(km)} km restantes" : null;
        if (!data.OnJob)
            return new PresenceText("Explorando libremente", Limit(Join(location, distance) ?? "Conduciendo por Europa"));

        var cargo = settings.ShowCargo && !string.IsNullOrWhiteSpace(data.Cargo) ? $"Entregando {data.Cargo}" : null;
        var route = settings.ShowRoute && !string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity)
            ? $"{data.SourceCity} → {data.DestinationCity}" : null;
        if (settings.RouteFirst && route is not null)
            return new PresenceText(Limit(route), Limit(Join(cargo, location, distance) ?? "Entrega en curso"));
        return new PresenceText(Limit(cargo ?? "Entrega en curso"), Limit(Join(route, location, distance) ?? "Euro Truck Simulator 2"));
    }

    private static string? Join(params string?[] fields)
    {
        var result = string.Join(" · ", fields.OfType<string>());
        return result.Length == 0 ? null : result;
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
