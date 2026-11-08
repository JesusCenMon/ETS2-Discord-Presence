namespace Ets2DiscordPresence;

internal static class DiscordIdentity
{
    internal const string ActivityName = "Euro Truck Simulator 2";
    private const string ArtRoot = "https://raw.githubusercontent.com/JesusCenMon/ETS2-Discord-Presence/v0.5.0/assets/";
    internal const string ActivityImage = ArtRoot + "hero-truck.png";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CountryImages = new(LoadCountryImages);

    internal static string ImageFor(string? country) =>
        country is not null && CountryImages.Value.TryGetValue(country, out var image) ? image : ActivityImage;

    private static IReadOnlyDictionary<string, string> LoadCountryImages()
    {
        using var file = AppResources.Open("country_art.json");
        var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(file) ?? [];
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, image) in values)
            if (Countries.Normalize(code) is { } key && image is { Length: > 0 and <= 256 }
                && Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                images[key] = image;
        return images;
    }

    internal static string ActivityBadge(string state) => ArtRoot + $"discord/{state}.png";
    internal const string ApplicationId = "1568840287428808846";
}
