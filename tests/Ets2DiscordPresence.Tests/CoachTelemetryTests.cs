using System.Text;
using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class CoachTelemetryTests
{
    [Theory]
    [InlineData("MAPA DEL MUNDO\nRestablecer navegación", "Map")]
    [InlineData("World Map · Set waypoint", "Map")]
    [InlineData("Modo foto — Profundidad de campo — Tomar fotografía", "Photo")]
    [InlineData("PHOTO MODE\nDepth of Field\nTake photo", "Photo")]
    [InlineData("Mapa del mundo · Conducir · Modo foto", null)]
    [InlineData("Juego en pausa", null)]
    public void ClassifiesScreensOnlyWhenTheirControlsArePresent(string text, string? expected) =>
        Assert.Equal(expected, GameScreenReader.Classify(text)?.ToString());

    [Theory]
    [InlineData("Photo", false, "Tomando fotos")]
    [InlineData("Map", true, "Mirando el mapa")]
    [InlineData("Customize", true, "Personalizando el autobús")]
    [InlineData("Customize", false, "Personalizando el camión")]
    public void NamesGameScreenForTheVehicle(string screen, bool coach, string title) =>
        Assert.Equal(title, PresenceFormatter.ScreenTitle(Enum.Parse<GameScreen>(screen), coach, english: false));

    private static readonly TelemetrySnapshot Freight = new(false, true, "Maquinaria", "Lyon", "Turín", 0, 0, 48_000, 12,
        Truck: new TruckInfo("Volvo", "FH16", 200, 600));

    [Fact]
    public void SdkPassengerRouteReplacesFreightFieldsAndHonorsPrivacy()
    {
        var frame = new byte[304];
        BitConverter.GetBytes(1u).CopyTo(frame, 4);
        BitConverter.GetBytes(1u).CopyTo(frame, 8);
        BitConverter.GetBytes(95u).CopyTo(frame, 12);
        Encoding.UTF8.GetBytes("Dijón").CopyTo(frame, 16);
        Encoding.UTF8.GetBytes("París").CopyTo(frame, 112);
        Encoding.UTF8.GetBytes("paris").CopyTo(frame, 208);
        var trip = CoachTelemetry.Apply(Freight, CoachTelemetry.Parse(frame));
        Assert.True(trip.Coach);
        Assert.True(trip.OnJob);
        Assert.Empty(trip.Cargo);
        Assert.Equal(95u, trip.PlannedDistanceKm);
        var shown = PresenceFormatter.Format(trip, null, country: "FR");
        Assert.Equal("Ruta de pasajeros", shown.Details);
        Assert.Contains("Dijón → París", shown.State);
        Assert.Contains("48 km", shown.State);
        var hidden = PresenceFormatter.Format(trip, "París", new AppSettings(ShowCity: false, ShowRoute: false, ShowDistance: false), "FR");
        Assert.Equal(DiscordIdentity.ActivityName, hidden.State);
        frame[8] = 0;
        Assert.Null(CoachTelemetry.Parse(frame));
        Assert.Same(Freight, CoachTelemetry.Apply(Freight, null));
    }

    [Theory]
    [InlineData("Scania", "Touring")]
    [InlineData("Volvo", "Volvo 9700 DD")]
    [InlineData("MAN", "Lion’s Coach")]
    public void DetectsOfficialCoachModelsDuringFreeDrive(string brand, string model)
    {
        var trip = CoachTelemetry.Apply(Freight with { OnJob = false, Truck = new(brand, model, 100, 300) }, null);
        Assert.True(trip.Coach);
        Assert.False(trip.OnJob);
        Assert.Equal("Conduciendo un autobús", PresenceFormatter.Format(trip, null).Details);
        Assert.Equal("Driving a coach", PresenceFormatter.Format(trip, null, new AppSettings(DiscordLanguage: "en")).Details);
    }

    [Fact]
    public void PreparesCoachPluginBesideAnExistingRenCloudInstallation()
    {
        var game = Directory.CreateTempSubdirectory("ets2-coaches-");
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(game.FullName, "bin", "win_x64"));
            File.WriteAllBytes(Path.Combine(bin.FullName, "eurotrucks2.exe"), []);
            var plugins = bin.CreateSubdirectory("plugins");
            var existing = Path.Combine(plugins.FullName, "scs-telemetry.dll");
            using (var resource = AppResources.Open("rencloud-telemetry.dll"))
            using (var target = File.Create(existing)) resource.CopyTo(target);
            Assert.True(PluginInstaller.Prepare(game.FullName));
            Assert.False(PluginInstaller.Prepare(game.FullName));
            var coach = Path.Combine(plugins.FullName, "ets2-discord-coaches.dll");
            Assert.True(File.Exists(coach));
            PluginInstaller.Maintain(game.FullName, remove: true);
            Assert.False(File.Exists(coach));
            Assert.True(File.Exists(existing));
        }
        finally { game.Delete(recursive: true); }
    }
}
