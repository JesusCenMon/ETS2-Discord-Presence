using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ets2DiscordPresence;

internal sealed record AppSettings(string GamePath)
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ETS2DiscordPresence", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new("")
                : new("");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new(""); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class SteamLocator
{
    public static string? FindGame()
    {
        var steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
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

    public static bool IsValidGamePath(string path) =>
        File.Exists(Path.Combine(path, "bin", "win_x64", "eurotrucks2.exe"));
}

internal static class PluginInstaller
{
    private const string InstalledName = "ets2-discord-telemetry.dll";

    public static bool Prepare(string gamePath)
    {
        if (!SteamLocator.IsValidGamePath(gamePath)) throw new InvalidOperationException("Selecciona la carpeta correcta de ETS2.");
        var directory = Path.Combine(gamePath, "bin", "win_x64", "plugins");
        var destination = Path.Combine(directory, InstalledName);
        using var source = AppResources.Open("rencloud-telemetry.dll");
        var expected = SHA256.HashData(source);
        source.Position = 0;
        foreach (var existing in new[] { destination, Path.Combine(directory, "scs-telemetry.dll") })
        {
            if (!File.Exists(existing)) continue;
            using var file = File.OpenRead(existing);
            if (SHA256.HashData(file).AsSpan().SequenceEqual(expected)) return false;
            throw new IOException("ETS2 ya tiene un lector de viaje de otra versión. Revisa los complementos del juego.");
        }

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
