using System.Text.Json;

namespace Ets2DiscordPresence;

internal static class StartupDiagnostics
{
    public static int Run(string[] args)
    {
        var resources = new Dictionary<string, bool>();
        var required = new[]
        {
            "cities_default.json", "country_art.json", "country_art_en.json", "hero-truck.png", "coach-art.coach.jpg", "truck-art.jpg",
            "ets2-presence.ico", "rencloud-telemetry.dll", "FLUENT-LICENSE.txt",
            "RENCLOUD-LICENSE.txt", "CITY-DATA-LICENSE.txt", "coach-telemetry.dll", "SCS-SDK-LICENSE.txt"
        };
        var icons = new[] { "book_open", "code", "desktop", "eye", "home", "info", "options", "play_circle",
            "question_circle", "settings", "vehicle_truck", "weather_moon", "weather_sunny" };
        foreach (var name in required.Concat(icons.Select(icon => $"fluent.{icon}.png")))
        {
            try
            {
                using var stream = AppResources.Open(name);
                resources[name] = stream.Length > 0;
                if (name is "cities_default.json") _ = new CityLookup(stream);
                if (name is "country_art.json" or "country_art_en.json") _ = DiscordIdentity.ReadCountryImages(stream);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or ArgumentException)
            {
                resources[name] = false;
            }
        }
        var available = CanWriteData();
        var settings = AppSettings.Load();
        if (args.Contains("--diagnostics-json"))
        {
            var report = JsonSerializer.Serialize(new
            {
                version = DesktopServices.Version.ToString(3),
                resources,
                dataAvailable = available,
                configuration = new { portable = LocalData.IsPortable, readerEnabled = !settings.ReaderDisabled,
                    sharing = settings.Sharing, previewUpdates = settings.PreviewUpdates }
            });
            var index = Array.IndexOf(args, "--output");
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1], report);
            else Console.WriteLine(report);
        }
        return available && resources.Values.All(value => value) ? 0 : 1;
    }

    private static bool CanWriteData()
    {
        try
        {
            var folder = LocalData.Folder;
            while (!Directory.Exists(folder) && Path.GetDirectoryName(folder) is { } parent)
                folder = parent;
            using var test = new FileStream(Path.Combine(folder, $"ets2-check-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
            return test.CanWrite;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
