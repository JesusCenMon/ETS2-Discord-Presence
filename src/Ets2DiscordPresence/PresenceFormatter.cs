using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed record PresenceText(string Details, string State, DateTime? Started = null, string? Image = null);
internal sealed record MapCity(string Name, double X, double Z, string? Id, string? Country = null);

internal sealed class CityLookup
{
    private const int CellSize = 8000;
    private readonly List<MapCity> cities;
    private readonly Dictionary<(int X, int Z), List<MapCity>> cells = [];
    public IReadOnlyList<MapCity> Cities => cities;
    public int Rejected { get; }
    public int DuplicateIds { get; }
    public int DuplicatePositions { get; }
    public int Identified => cities.Count(city => !string.IsNullOrWhiteSpace(city.Id));
    public int RecognizedCountries => cities.Select(city => city.Country).OfType<string>().Distinct().Count();
    public MapCity? FindDestination(string name, string? id) =>
        (string.IsNullOrWhiteSpace(id) ? null : cities.Find(city => string.Equals(city.Id, id, StringComparison.OrdinalIgnoreCase)))
        ?? cities.Find(city => CultureInfo.CurrentCulture.CompareInfo.Compare(city.Name, name,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0);

    public CityLookup(Stream source)
    {
        cities = new();
        using var document = JsonDocument.Parse(source);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<string, List<MapCity>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.RootElement.GetProperty("citiesList").EnumerateArray())
        {
            var name = StringProperty(item, "realName");
            if (string.IsNullOrWhiteSpace(name) || !item.TryGetProperty("x", out var xValue)
                || !TryCoordinate(xValue, out var x) || !item.TryGetProperty("z", out var zValue) || !TryCoordinate(zValue, out var z))
            {
                Rejected++;
                continue;
            }
            var id = StringProperty(item, "gameName")?.Trim();
            var city = new MapCity(name.Trim(), x, z, id, Countries.Normalize(StringProperty(item, "country")));
            if (!string.IsNullOrWhiteSpace(id) && !ids.Add(id)) DuplicateIds++;
            if (!positions.TryGetValue(city.Name, out var matching)) positions[city.Name] = matching = [];
            if (matching.Any(other => Math.Abs(other.X - x) <= 20 && Math.Abs(other.Z - z) <= 20)) DuplicatePositions++;
            matching.Add(city);
            cities.Add(city);
            var cell = (Cell(x), Cell(z));
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = [];
            bucket.Add(city);
            if (cities.Count > 20_000) throw new InvalidDataException("Catálogo de ciudades demasiado grande.");
        }
        if (cities.Count == 0 || cities.Count > 20_000) throw new InvalidDataException("Catálogo de ciudades vacío o demasiado grande.");
    }

    private static string? StringProperty(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Cell(double value) => (int)Math.Floor(value / CellSize);

    private IEnumerable<MapCity> Candidates(double x, double z, double range)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || Math.Abs(x) > 2_100_000 || Math.Abs(z) > 2_100_000) yield break;
        for (var column = Cell(x - range); column <= Cell(x + range); column++)
            for (var row = Cell(z - range); row <= Cell(z + range); row++)
                if (cells.TryGetValue((column, row), out var bucket))
                    foreach (var city in bucket) yield return city;
    }

    private static bool TryCoordinate(JsonElement value, out double number) =>
        double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number) && Math.Abs(number) <= 2_000_000;

    public string? FindCountry(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) return null;
        const double range = 14_000;
        var nearest = Candidates(x, z, range).Select(city => (City: city, Distance: Math.Sqrt((city.X - x) * (city.X - x) + (city.Z - z) * (city.Z - z))))
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
        foreach (var city in Candidates(x, z, 8000))
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

internal static class PresencePrivacy
{
    internal static AppSettings Apply(AppSettings settings, bool? onJob, bool hideDestination)
    {
        if (settings.Features.DiscordMode == "private")
            return settings with { ShowCity = false, ShowCountry = false, ShowRoute = false, ShowCargo = false, ShowDistance = false,
                Options = settings.Features with { DeliveryTemplate = null, RoamTemplate = null } };
        if (settings.Features.DiscordMode == "stream")
            settings = settings with { ShowRoute = false, ShowCargo = false, ShowDistance = false,
                Options = settings.Features with { DeliveryTemplate = null, RoamTemplate = null } };
        if (hideDestination || onJob == true && settings.Features.HideDeliveryLocation)
            return settings with { ShowCity = false, ShowCountry = false, ShowRoute = false };
        if (onJob == false && settings.Features.HideRoamingLocation) return settings with { ShowCity = false, ShowCountry = false };
        return settings;
    }

    internal static (string Route, string Distance) Overlay(AppSettings visibility, TelemetrySnapshot? trip,
        bool sharing, string idle, string distance)
    {
        if (!sharing || !visibility.ShowRoute) return ("", "");
        if (trip is not { OnJob: true, Paused: false })
            return (string.IsNullOrWhiteSpace(visibility.Features.ObsWaiting) ? idle : visibility.Features.ObsWaiting, "");
        return ($"{trip.SourceCity}  →  {trip.DestinationCity}", visibility.ShowDistance ? distance : "");
    }
}

internal static class PresenceFormatter
{
    internal static string ScreenTitle(GameScreen screen, bool coach, bool english) => (screen, coach, english) switch
    {
        (GameScreen.Photo, _, false) => "Tomando fotos",
        (GameScreen.Photo, _, true) => "Taking photos",
        (GameScreen.Map, _, false) => "Mirando el mapa",
        (GameScreen.Map, _, true) => "Viewing the map",
        (GameScreen.Customize, true, false) => "Personalizando el autobús",
        (GameScreen.Customize, true, true) => "Customizing the coach",
        (_, _, false) => "Personalizando el camión",
        _ => "Customizing the truck"
    };

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
        if (data.OnJob && settings.SharesCountry && settings.Features.DiscordMode == "stream" && Countries.Name(country, english) is { } coarse)
            location = T($"Conduciendo por {coarse}", $"Driving through {coarse}");
        var distance = settings.ShowDistance && data.RemainingKm is { } km
            ? T($"{AppOptions.Distance(Math.Ceiling(km), settings.Features.Miles, "0")} restantes",
                $"{AppOptions.Distance(Math.Ceiling(km), settings.Features.Miles, "0")} remaining") : null;
        var cargo = data.OnJob && settings.ShowCargo && !string.IsNullOrWhiteSpace(data.Cargo)
            ? T($"Entregando {data.Cargo}", $"Delivering {data.Cargo}") : null;
        string? route = null;
        if (data.OnJob && settings.ShowRoute)
        {
            if (!string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity))
                route = $"{data.SourceCity} → {data.DestinationCity}";
            else if (data.Coach && !string.IsNullOrWhiteSpace(data.DestinationCity))
                route = T($"Destino {data.DestinationCity}", $"To {data.DestinationCity}");
        }
        var fields = Order(settings).Select(key => key switch
        {
            "cargo" => cargo, "route" => route, "city" => location, "distance" => distance, _ => null
        }).OfType<string>().ToArray();
        if (data.Coach)
        {
            var detailsCoach = data.OnJob ? T("Ruta de pasajeros", "Passenger route")
                : T("Conduciendo un autobús", "Driving a coach");
            var countryName = settings.SharesCountry ? Countries.Name(country, english) : null;
            var locationCoach = countryName is null || data.OnJob && location is not null ? null
                : T($"Conduciendo por {countryName}", $"Driving through {countryName}");
            var stateCoach = string.Join(" · ", (data.OnJob ? fields.Append(locationCoach) : fields.Prepend(locationCoach)).OfType<string>());
            return new PresenceText(detailsCoach, Limit(stateCoach.Length > 0 ? stateCoach : DiscordIdentity.ActivityName));
        }
        if (!data.OnJob)
        {
            var name = settings.SharesCountry ? Countries.Name(country, english) : null;
            var roaming = name is null ? T("Conducción libre", "Free driving") : T($"Conduciendo por {name}", $"Driving through {name}");
            var headline = Template(settings.Features.RoamTemplate, data, settings, name);
            return new PresenceText(headline is null ? T("Explorando libremente", "Free roaming") : Limit(headline), Limit(string.Join(" · ", fields.Prepend(roaming))));
        }
        var template = Template(settings.Features.DeliveryTemplate, data, settings,
            settings.SharesCountry ? Countries.Name(country, english) : null);
        var stream = settings.Features.DiscordMode == "stream";
        var details = template ?? (stream ? null : fields.FirstOrDefault()) ?? T("Entrega en curso", "Delivery in progress");
        var state = template is null && !stream ? fields.Skip(1) : fields.AsEnumerable();
        var line = string.Join(" · ", state);
        return new PresenceText(Limit(details), Limit(line.Length == 0 ? DiscordIdentity.ActivityName : line));
    }

    private static readonly System.Text.RegularExpressions.Regex Conditional = new(@"\{if:([^{}|]+)\}([^{}]*(?:\{(?!/?if\b)[^{}]+\}[^{}]*)*)\{/if\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex Placeholder = new(@"\{([^{}|]+)(?:\|([^{}]*))?\}");
    private static readonly HashSet<string> Variables = new(StringComparer.OrdinalIgnoreCase)
        { "país", "country", "camión", "truck", "carga", "cargo", "destino", "destination" };

    internal static string[] UnknownVariables(string template) => Placeholder.Matches(template)
        .Select(match => match.Groups[1].Value)
        .Where(name => !name.Equals("/if", StringComparison.OrdinalIgnoreCase)
            && !Variables.Contains(name.StartsWith("if:", StringComparison.OrdinalIgnoreCase) ? name[3..] : name))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string? Template(string? template, TelemetrySnapshot data, AppSettings settings, string? country)
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
        var selected = Conditional.Replace(template, match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) && !string.IsNullOrWhiteSpace(value) ? match.Groups[2].Value : "");
        var result = Placeholder.Replace(selected, match =>
            variables.TryGetValue(match.Groups[1].Value, out var value)
                ? string.IsNullOrWhiteSpace(value) ? match.Groups[2].Value : value : "").Trim();
        return result.Length < 2 ? null : result;
    }

    internal static string Limit(string text)
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
