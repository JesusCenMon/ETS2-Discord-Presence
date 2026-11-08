using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ets2DiscordPresence;

internal sealed record AppSettings(string DiscordAppId, string GamePath)
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ETS2DiscordPresence", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new("", "")
                : new("", "");
        }
        catch (JsonException) { return new("", ""); }
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
    private const string Url = "https://github.com/RenCloud/scs-sdk-plugin/releases/download/V.1.12.1/release_v_1_12_1.zip";
    private const string ExpectedSha256 = "3679EC81407DE0EBFB45241E496E2AD31E30A6E409EF0246FC86195BFEE9C9A5";
    private const string ZipEntry = "release_v_1_12_1/Win64/scs-telemetry.dll";
    private const string InstalledName = "ets2-discord-telemetry.dll";

    public static bool IsInstalled(string gamePath) =>
        File.Exists(Path.Combine(gamePath, "bin", "win_x64", "plugins", InstalledName));

    public static async Task<string> InstallAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        if (!SteamLocator.IsValidGamePath(gamePath)) throw new InvalidOperationException("Selecciona la carpeta correcta de ETS2.");
        if (Process.GetProcessesByName("eurotrucks2").Length > 0)
            throw new InvalidOperationException("Cierra ETS2 antes de instalar el componente.");
        var directory = Path.Combine(gamePath, "bin", "win_x64", "plugins");
        var destination = Path.Combine(directory, InstalledName);
        if (File.Exists(destination)) return "El componente ya está instalado. Abre ETS2.";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var archive = await client.GetByteArrayAsync(Url, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(archive));
        if (!hash.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La descarga no pasó la comprobación de integridad.");
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var entry = zip.GetEntry(ZipEntry) ?? throw new InvalidDataException("No se encontró el componente en la descarga.");
        Directory.CreateDirectory(directory);
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
        await using (var input = entry.Open())
            await input.CopyToAsync(output, cancellationToken);
        return "Componente instalado. Abre o reinicia ETS2.";
    }
}
