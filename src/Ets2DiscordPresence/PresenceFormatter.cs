using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record PresenceText(string Details, string State, DateTime? Started = null, string? Image = null, string? Badge = null);
internal sealed record MapCity(string Name, double X, double Z, string? Id, string? Country = null);

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
                cities.Add(new MapCity(name.Trim(), x, z, item.TryGetProperty("gameName", out var id) ? id.GetString() : null,
                    item.TryGetProperty("country", out var country) && country.ValueKind == JsonValueKind.String
                        ? Countries.Normalize(country.GetString()) : null));
        }
        if (cities.Count == 0 || cities.Count > 20_000) throw new InvalidDataException("Catálogo de ciudades vacío o demasiado grande.");
    }

    private static bool TryCoordinate(JsonElement value, out double number) =>
        double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number) && Math.Abs(number) <= 2_000_000;

    public string? FindCountry(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) return null;
        const double range = 14_000;
        var nearest = cities.Select(city => (City: city, Distance: Math.Sqrt((city.X - x) * (city.X - x) + (city.Z - z) * (city.Z - z))))
            .Where(candidate => candidate.Distance <= range).OrderBy(candidate => candidate.Distance);
        using var matches = nearest.GetEnumerator();
        if (!matches.MoveNext() || matches.Current.City.Country is not { } country) return null;
        var distance = matches.Current.Distance;
        while (matches.MoveNext())
        {
            if (matches.Current.Distance >= distance + 650) break;
            if (matches.Current.City.Country is { } other && other != country) return null;
        }
        return country;
    }

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

    public static PresenceText Format(TelemetrySnapshot data, string? city, AppSettings? settings = null, string? country = null)
    {
        settings ??= new AppSettings();
        var english = settings.DiscordLanguage == "en";
        string T(string es, string en) => english ? en : es;
        if (data.Paused) return new PresenceText(T("Juego en pausa", "Game paused"), "Euro Truck Simulator 2");
        var location = city is null || !settings.ShowCity ? null : T($"Cerca de {city}", $"Near {city}");
        var distance = settings.ShowDistance && data.RemainingKm is { } km
            ? T($"{AppOptions.Distance(Math.Ceiling(km), settings.Features.Miles, "0")} restantes",
                $"{AppOptions.Distance(Math.Ceiling(km), settings.Features.Miles, "0")} remaining") : null;
        var cargo = data.OnJob && settings.ShowCargo && !string.IsNullOrWhiteSpace(data.Cargo)
            ? T($"Entregando {data.Cargo}", $"Delivering {data.Cargo}") : null;
        var route = data.OnJob && settings.ShowRoute && !string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity)
            ? $"{data.SourceCity} → {data.DestinationCity}" : null;
        var fields = Order(settings).Select(key => key switch
        {
            "cargo" => cargo, "route" => route, "city" => location, "distance" => distance, _ => null
        }).OfType<string>().ToArray();
        if (!data.OnJob)
        {
            var name = settings.ShowCity ? Countries.Name(country, english) : null;
            var roaming = name is null ? T("Conducción libre", "Free driving") : T($"Conduciendo por {name}", $"Driving through {name}");
            var headline = Template(settings.Features.RoamTemplate, data, settings, name);
            return new PresenceText(headline ?? T("Explorando libremente", "Free roaming"), Limit(string.Join(" · ", fields.Prepend(roaming))));
        }
        var template = Template(settings.Features.DeliveryTemplate, data, settings,
            settings.ShowCity ? Countries.Name(country, english) : null);
        var details = template ?? fields.FirstOrDefault() ?? T("Entrega en curso", "Delivery in progress");
        var state = template is null ? fields.Skip(1) : fields.AsEnumerable();
        var line = string.Join(" · ", state);
        return new PresenceText(Limit(details), Limit(line.Length == 0 ? DiscordIdentity.ActivityName : line));
    }

    private static string? Template(string? template, TelemetrySnapshot data, AppSettings settings, string? country)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        var truck = string.Join(' ', new[] { data.Truck?.Brand, data.Truck?.Model }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var cargo = settings.ShowCargo && data.OnJob ? data.Cargo : "";
        var destination = settings.ShowRoute && data.OnJob ? data.DestinationCity : "";
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["país"] = country ?? "", ["country"] = country ?? "", ["camión"] = truck, ["truck"] = truck,
            ["carga"] = cargo, ["cargo"] = cargo, ["destino"] = destination, ["destination"] = destination
        };
        var result = System.Text.RegularExpressions.Regex.Replace(template, @"\{([^{}]+)\}", match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) ? value : "").Trim();
        return result.Length < 2 ? null : Limit(result);
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
