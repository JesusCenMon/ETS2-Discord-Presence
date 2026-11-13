namespace Ets2DiscordPresence;

internal sealed record DiscordProfile(bool Cargo, bool Route, bool City, bool Distance, bool RouteFirst,
    string[]? Order, string Language, bool Timer, bool Image, bool SessionTimer, string? Roam, string? Delivery,
    bool HideRoaming, bool HideDelivery, bool? Country = null)
{
    public static DiscordProfile Capture(AppSettings settings)
    {
        var options = settings.Features;
        return new(settings.ShowCargo, settings.ShowRoute, settings.ShowCity, settings.ShowDistance, settings.RouteFirst,
            settings.PresenceOrder, settings.DiscordLanguage, settings.ActivityTimer, settings.ActivityImage, options.SessionTimer,
            options.RoamTemplate, options.DeliveryTemplate, options.HideRoamingLocation, options.HideDeliveryLocation, settings.ShowCountry);
    }

    public AppSettings Apply(AppSettings settings) => settings with
    {
        ShowCargo = Cargo, ShowRoute = Route, ShowCity = City, ShowCountry = Country, ShowDistance = Distance, RouteFirst = RouteFirst,
        PresenceOrder = Order, DiscordLanguage = Language, ActivityTimer = Timer, ActivityImage = Image,
        Options = settings.Features with { SessionTimer = SessionTimer, RoamTemplate = Roam, DeliveryTemplate = Delivery,
            HideRoamingLocation = HideRoaming, HideDeliveryLocation = HideDelivery }
    };
}
