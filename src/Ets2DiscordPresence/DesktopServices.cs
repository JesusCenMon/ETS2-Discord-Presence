using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace Ets2DiscordPresence;

internal static class DesktopServices
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "ETS2DiscordPresence";
    private const string Repository = "JesusCenMon/ETS2-Discord-Presence";
    private const string Asset = "ETS2DiscordPresence.exe";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
        return key?.GetValue(StartupName) is string;
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
        if (enabled)
            key.SetValue(StartupName, $"\"{Application.ExecutablePath}\" --minimized");
        else
            key.DeleteValue(StartupName, throwOnMissingValue: false);
    }

    public static void Open(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();

    public static async Task<Version> CheckReaderVersionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/RenCloud/scs-sdk-plugin/releases/latest");
        request.Headers.UserAgent.ParseAdd("ETS2DiscordPresence");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("tag_name", out var tag)
            || !System.Version.TryParse(tag.GetString()?.TrimStart('v', 'V', '.', ' '), out var version))
            throw new FormatException("Invalid reader version.");
        return version;
    }

    public static async Task<AppUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=20");
        request.Headers.UserAgent.ParseAdd("ETS2DiscordPresence");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var current = new Version(Version.Major, Version.Minor, Version.Build);
        var updates = new List<AppUpdate>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!System.Version.TryParse(tag.TrimStart('v'), out var parsed) || parsed <= current) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != Asset) continue;
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                updates.Add(new AppUpdate(parsed, $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Asset}",
                    digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? digest[7..] : null));
            }
        }
        return updates.OrderByDescending(update => update.Version).FirstOrDefault();
    }
}
