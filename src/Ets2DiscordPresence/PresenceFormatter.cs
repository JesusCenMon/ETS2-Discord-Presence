using System.Globalization;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record PresenceText(string Details, string State);

internal sealed class CityLookup
{
    private readonly List<City> cities;
    private sealed record City(string Name, double X, double Z);

    public CityLookup(string path)
    {
        cities = new();
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
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
    public static PresenceText Format(TelemetrySnapshot data, string? city)
    {
        if (data.Paused) return new PresenceText("En pausa", "Euro Truck Simulator 2");
        var location = city is null ? null : $"Cerca de {city}";
        if (!data.OnJob)
            return new PresenceText("Explorando libremente", Limit(location ?? "Conduciendo por Europa"));

        var cargo = string.IsNullOrWhiteSpace(data.Cargo) ? "Entrega en curso" : $"Entregando {data.Cargo}";
        var route = !string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity)
            ? $"{data.SourceCity} → {data.DestinationCity}" : "Entrega en curso";
        var distance = float.IsFinite(data.RouteDistanceMeters) && data.RouteDistanceMeters > 0
            ? $"{Math.Ceiling(data.RouteDistanceMeters / 1000)} km restantes" : null;
        var state = string.Join(" · ", new[] { route, location, distance }.Where(x => x is not null));
        return new PresenceText(Limit(cargo), Limit(state));
    }

    private static string Limit(string text) => text.Length <= 128 ? text : text[..125] + "...";
}
