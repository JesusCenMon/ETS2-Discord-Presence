namespace Ets2DiscordPresence;

internal static class DiscordIdentity
{
    internal const string ActivityName = "Euro Truck Simulator 2";
    internal const string AssetsTag = "v0.8.1";
    internal const string ActivityImage = "https://raw.githubusercontent.com/JesusCenMon/ETS2-Discord-Presence/d068a4f120338d998ac4ec42dcf9e2708ca92762/assets/discord/truck.jpg";
    private const string CoachArtworkRoot = "https://raw.githubusercontent.com/JesusCenMon/ETS2-Discord-Presence/d068a4f120338d998ac4ec42dcf9e2708ca92762/assets/discord/coaches/";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> SpanishImages = new(() => LoadCountryImages("country_art.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishImages = new(() => LoadCountryImages("country_art_en.json"));

    internal static string ImageFor(string? country, bool releaseArtwork = true, bool english = false, bool coach = false)
    {
        var images = english ? EnglishImages : SpanishImages;
        if (country is not null && images.Value.TryGetValue(country, out var image))
            return coach ? CoachArtworkRoot + Path.GetFileName(image) : image;
        if (coach) return CoachArtworkRoot + "coach.jpg";
        return ActivityImage;
    }

    private static IReadOnlyDictionary<string, string> LoadCountryImages(string resource)
    {
        using var file = AppResources.Open(resource);
        return ReadCountryImages(file);
    }

    internal static string? CountryArtworkResource(string? image)
    {
        if (image is null) return null;
        var file = Path.GetFileName(image);
        if (image == ActivityImage) return "truck-art.jpg";
        if (image == CoachArtworkRoot + "coach.jpg") return "coach-art.coach.jpg";
        if (file.Length != 9 || !file.EndsWith(".jpg", StringComparison.Ordinal) || file[2] != '-'
            || Countries.Normalize(file[..2]) is not { } code) return null;
        var images = file[3..5] switch { "es" => SpanishImages, "en" => EnglishImages, _ => null };
        if (images?.Value.TryGetValue(code, out var published) != true) return null;
        if (published == image) return $"country-art.{file}";
        return image == CoachArtworkRoot + file ? $"coach-art.{file}" : null;
    }

    internal static IReadOnlyDictionary<string, string> ReadCountryImages(Stream file)
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(file);
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in manifest.RootElement.EnumerateObject())
        {
            if (Countries.Normalize(item.Name) != item.Name || item.Value.ValueKind != System.Text.Json.JsonValueKind.String
                || item.Value.GetString() is not { Length: > 0 and <= 256 } image
                || !Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || !images.TryAdd(item.Name, image))
                throw new InvalidDataException("Invalid or duplicate country artwork entry.");
        }
        return images;
    }

    internal const string ApplicationId = "1568840287428808846";
}
