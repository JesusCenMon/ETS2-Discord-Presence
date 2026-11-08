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

    public static async Task<(Version Version, string Url)?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=20");
        request.Headers.UserAgent.ParseAdd("ETS2DiscordPresence");
        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var current = new Version(Version.Major, Version.Minor, Version.Build);
        return document.RootElement.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean()
                && release.GetProperty("assets").EnumerateArray().Any(asset => asset.GetProperty("name").GetString() == Asset))
            .Select(release => release.GetProperty("tag_name").GetString() ?? "")
            .Select(tag => (Tag: tag, Parsed: System.Version.TryParse(tag.TrimStart('v'), out var parsed) ? parsed : null))
            .Where(release => release.Parsed is not null && release.Parsed > current)
            .OrderByDescending(release => release.Parsed)
            .Select(release => ((Version Version, string Url)?)(release.Parsed!,
                $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(release.Tag)}/{Asset}"))
            .FirstOrDefault();
    }
}
