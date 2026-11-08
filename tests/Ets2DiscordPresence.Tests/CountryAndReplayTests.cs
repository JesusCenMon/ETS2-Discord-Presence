using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class CountryAndReplayTests
{
    [Theory]
    [InlineData(-65398, 48701, "ES", "España")]
    [InlineData(-92711, 50654, "PT", "Portugal")]
    [InlineData(-39547, -11584, "GB", "el Reino Unido")]
    public void UsesCountryOfCurrentPositionInsteadOfEurope(double x, double z, string code, string name)
    {
        using var source = AppResources.Open("cities_default.json");
        var catalog = new CityLookup(source);
        Assert.Equal(code, catalog.FindCountry(x, z));
        var trip = new TelemetrySnapshot(false, false, "", "", "", x, z, 0, 12);
        var shown = PresenceFormatter.Format(trip, catalog.FindNearby(x, z), country: code);
        Assert.StartsWith($"Conduciendo por {name}", shown.State);
        Assert.Equal("Conducción libre", PresenceFormatter.Format(trip, "Madrid",
            new AppSettings(ShowCity: false), code).State);
        Assert.Equal(DiscordIdentity.ActivityImage, DiscordIdentity.ImageFor(null));
    }

    [Fact]
    public void DoesNotGuessOnBoundaryOrBeyondKnownCities()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            {"citiesList":[{"realName":"A","x":0,"z":0,"country":"ES"},
            {"realName":"B","x":2500,"z":0,"country":"PT"}]}
            """));
        var catalog = new CityLookup(stream);
        Assert.Equal("ES", catalog.FindCountry(0, 0));
        Assert.Null(catalog.FindCountry(1250, 0));
        Assert.Null(catalog.FindCountry(70_000, 0));
    }

    [Fact]
    public void TemplatesCannotReadLocationFieldsHiddenInDiscord()
    {
        var job = new TelemetrySnapshot(false, true, "Metal", "Lyon", "Madrid", 0, 0, 0, 12,
            Truck: new TruckInfo("DAF", "XF", 100, 400));
        var config = new AppSettings(ShowCity: false, ShowRoute: false, ShowCargo: false,
            Options: new AppOptions(DeliveryTemplate: "{camión} {país} {carga} {destino}"));
        var result = PresenceFormatter.Format(job, "Madrid", config, "ES");
        Assert.Equal("DAF XF", result.Details);
        Assert.DoesNotContain("Madrid", result.State);
    }

    [Fact]
    public void RecoversPartialFileAndTrimsWithRebasedMarkers()
    {
        var file = Path.GetTempFileName();
        try
        {
            using (var writer = File.CreateText(file))
            {
                TelemetryReplay.Write(writer, new RecordedFrame(0, null));
                TelemetryReplay.Write(writer, new RecordedFrame(2000, null, "Ramp"));
                TelemetryReplay.Write(writer, new RecordedFrame(4000, null));
                writer.Write("{");
            }
            var recovered = TelemetryReplay.Read(file);
            var trimmed = TelemetryReplay.Trim(recovered, 1000, 3500);
            Assert.Equal(new long[] { 0, 1000, 2500 }, trimmed.Select(frame => frame.ElapsedMs));
            Assert.Equal((1000L, "Ramp"), Assert.Single(TelemetryReplay.Milestones(trimmed)));
        }
        finally { File.Delete(file); }
    }
}
