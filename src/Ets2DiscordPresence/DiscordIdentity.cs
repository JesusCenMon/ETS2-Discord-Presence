namespace Ets2DiscordPresence;

internal static class DiscordIdentity
{
    // Public application ID for ETS2 Presence in the Discord Developer Portal.
    private const string ArtRoot = "https://raw.githubusercontent.com/JesusCenMon/ETS2-Discord-Presence/v0.5.0/assets/";
    internal const string ActivityImage = ArtRoot + "hero-truck.png";
    internal static string ActivityBadge(string state) => ArtRoot + $"discord/{state}.png";
    internal const string ApplicationId = "1568840287428808846";
}
