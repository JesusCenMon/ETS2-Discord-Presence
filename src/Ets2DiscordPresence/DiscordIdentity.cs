namespace Ets2DiscordPresence;

internal static class DiscordIdentity
{
    internal const string ActivityName = "Euro Truck Simulator 2";
    internal const string AssetsTag = "v0.7.2";
    private const string ArtRoot = "https://raw.githubusercontent.com/JesusCenMon/ETS2-Discord-Presence/" + AssetsTag + "/assets/";
    internal const string ActivityImage = ArtRoot + "hero-truck.png";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CountryImages = new(LoadCountryImages);

    internal static string ImageFor(string? country) =>
        country is not null && CountryImages.Value.TryGetValue(country, out var image) ? image : ActivityImage;

    private static IReadOnlyDictionary<string, string> LoadCountryImages()
    {
        using var file = AppResources.Open("country_art.json");
        return ReadCountryImages(file);
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

    internal static string ActivityBadge(string state) => ArtRoot + $"discord/{state}.png";
    internal const string ApplicationId = "1568840287428808846";
}
