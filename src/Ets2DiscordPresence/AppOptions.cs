namespace Ets2DiscordPresence;

internal sealed record AppOptions(
    string Layout = "full", bool Miles = false, bool Clock12 = false, string AlertMode = "normal",
    bool HighContrast = false, bool ReconnectNotice = false, bool RestNotice = false, bool SessionTimer = false,
    bool HideRoamingLocation = false, bool PauseShortcut = false, string[]? Pinned = null,
    bool LowPower = false, bool AutoRecord = false, int RecordingLimitMb = 0, string? BackupFolder = null,
    int BackupIntervalDays = 7, string? ObsWaiting = null, int ObsSize = 100, bool ObsDistance = true,
    bool ObsRoute = true, string ObsPosition = "left", Rectangle? FloatingBounds = null,
    string? RoamTemplate = null, string? DeliveryTemplate = null, bool ObsBrowser = false, bool HideDeliveryLocation = false)
{
    public static string Distance(double km, bool miles, string format = "N0") =>
        $"{(miles ? km / 1.609344 : km).ToString(format)} {(miles ? "mi" : "km")}";
}
