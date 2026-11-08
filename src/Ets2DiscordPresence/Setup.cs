using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ets2DiscordPresence;

internal sealed record AppSettings(string GamePath = "", bool Sharing = true, bool ShowCargo = true,
    bool ShowRoute = true, bool ShowCity = true, bool ShowDistance = true,
    bool RouteFirst = false, bool Compact = false, string Language = "es", string DiscordLanguage = "es",
    string Theme = "system", bool GuideDismissed = false, bool ReaderDisabled = false,
    string? CityCatalog = null, string Profile = "custom", string[]? PresenceOrder = null,
    string[]? PanelOrder = null, string[]? HiddenPanels = null,
    string DriverProfile = "", string[]? Drivers = null, int TextPercent = 100, int RetentionDays = 0,
    int FuelThreshold = 0, int PrivacyShortcut = 0, bool ActivityImage = true, bool ActivityTimer = true, bool ShowArtwork = false, AppOptions? Options = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public AppOptions Features => Options ?? new();

    private static string SettingsPath => Path.Combine(LocalData.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) is { DriverProfile: not null } saved ? saved with { DriverProfile = saved.DriverProfile.Trim() } : new("")
                : new("");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(""); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal static class SteamLocator
{
    public static string? FindGame()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var steamPath = steam?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath)) return null;
        steamPath = steamPath.Replace('/', '\\');
        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            var content = File.ReadAllText(vdf);
            foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var game = Path.Combine(library, "steamapps", "common", "Euro Truck Simulator 2");
            if (File.Exists(Path.Combine(game, "bin", "win_x64", "eurotrucks2.exe"))) return game;
        }
        return null;
    }

    public static bool IsValidGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "bin", "win_x64", "eurotrucks2.exe"));
}

internal static class PluginInstaller
{
    internal const string BundledVersion = "1.12.1";
    private const string InstalledName = "ets2-discord-telemetry.dll";

    public static void Maintain(string gamePath, bool remove)
    {
        if (!SteamLocator.IsValidGamePath(gamePath)) throw new IOException("ETS2 no está localizado.");
        var destination = Path.Combine(gamePath, "bin", "win_x64", "plugins", InstalledName);
        if (remove)
        {
            using var source = AppResources.Open("rencloud-telemetry.dll");
            if (!File.Exists(destination)) return;
            using (var installed = File.OpenRead(destination))
                if (!SHA256.HashData(installed).AsSpan().SequenceEqual(SHA256.HashData(source)))
                    throw new IOException("El archivo cambió. Revísalo manualmente para conservar complementos ajenos.");
            File.Delete(destination);
            return;
        }

        if (!File.Exists(destination))
        {
            Prepare(gamePath);
            return;
        }
        using var replacement = AppResources.Open("rencloud-telemetry.dll");
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew)) replacement.CopyTo(output);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public static bool Prepare(string gamePath)
    {
        if (!SteamLocator.IsValidGamePath(gamePath)) throw new InvalidOperationException("Selecciona la carpeta correcta de ETS2.");
        var directory = Path.Combine(gamePath, "bin", "win_x64", "plugins");
        var destination = Path.Combine(directory, InstalledName);
        using var source = AppResources.Open("rencloud-telemetry.dll");
        var expected = SHA256.HashData(source);
        source.Position = 0;
        var compatibleFound = false;
        foreach (var existing in new[] { destination, Path.Combine(directory, "scs-telemetry.dll") })
        {
            if (!File.Exists(existing)) continue;
            using var file = File.OpenRead(existing);
            if (!SHA256.HashData(file).AsSpan().SequenceEqual(expected))
                throw new IOException($"Lector incompatible: {Path.GetFileName(existing)}. Cierra ETS2, mueve ese archivo fuera de plugins si ya no lo usas y vuelve a abrir esta app.");
            if (compatibleFound)
                throw new IOException("Hay dos lectores de ETS2. Cierra el juego, conserva solo uno y pulsa Reintentar preparación.");
            compatibleFound = true;
        }
        if (compatibleFound) return false;

        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".ets2-presence-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                source.CopyTo(output);
            File.Move(temporary, destination);
            return true;
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

internal static class AppResources
{
    public static Stream Open(string name) =>
        typeof(AppResources).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidDataException($"Falta el recurso {name}.");
}
