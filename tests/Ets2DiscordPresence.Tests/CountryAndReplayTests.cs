using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class CountryAndReplayTests
{
    [Theory]
    [InlineData(-65398, 48701, "ES", "España")]
    [InlineData(-92711, 50654, "PT", "Portugal")]
    [InlineData(-39547, -11584, "GB", "el Reino Unido")]
    [InlineData(-73031.1, -98960.5, "IS", "Islandia")]
    [InlineData(-58000, -102000, "IS", "Islandia")]
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
    public void CountryCanBeSharedWithoutCityAndIsHiddenByPrivacyRules()
    {
        var trip = new TelemetrySnapshot(false, false, "", "", "", 0, 0, 0, 12);
        var coarse = new AppSettings(ShowCity: false, ShowCountry: true);
        Assert.Equal("Conduciendo por Alemania", PresenceFormatter.Format(trip, "Nürnberg", coarse, "DE").State);
        Assert.Equal("Conducción libre", PresenceFormatter.Format(trip, "Nürnberg",
            PresencePrivacy.Apply(coarse, false, true), "DE").State);
        Assert.False(PresencePrivacy.Apply(coarse with { Options = new AppOptions(DiscordMode: "private") }, false, false).SharesCountry);
        Assert.True(DiscordProfile.Capture(coarse).Apply(new AppSettings()).SharesCountry);
    }

    [Fact]
    public void EachBundledCityHasCountryArtworkInBothLanguages()
    {
        using var source = AppResources.Open("cities_default.json");
        var catalog = new CityLookup(source);
        Assert.All(catalog.Cities, city => Assert.NotNull(city.Country));
        foreach (var country in catalog.Cities.Select(city => city.Country!).Distinct())
        foreach (var english in new[] { false, true })
        foreach (var coach in new[] { false, true })
        {
            var artwork = DiscordIdentity.ImageFor(country, english: english, coach: coach);
            Assert.EndsWith($"/{country.ToLowerInvariant()}-{(english ? "en" : "es")}.jpg", artwork);
        }
    }

    [Fact]
    public void ShowsGermanCoachArtworkNearNuremberg()
    {
        using var source = AppResources.Open("cities_default.json");
        var catalog = new CityLookup(source);
        var country = catalog.FindCountry(3230.73, 8705.79);
        Assert.Equal("DE", country);
        Assert.EndsWith("/coaches/de-en.jpg", DiscordIdentity.ImageFor(country, english: true, coach: true));
        Assert.EndsWith("/coaches/de-es.jpg", DiscordIdentity.ImageFor(country, coach: true));
    }

    [Fact]
    public void IcelandCitiesAndLocalizedCountryArtworkAreBundled()
    {
        using var source = AppResources.Open("cities_default.json");
        var cities = new CityLookup(source);
        var iceland = cities.Cities.Where(city => city.Country == "IS").ToArray();
        Assert.Equal(14, iceland.Length);
        Assert.All(iceland, city =>
        {
            Assert.Equal(city, cities.FindDestination("", city.Id));
            Assert.Equal("IS", cities.FindCountry(city.X, city.Z));
        });

        var spanish = DiscordIdentity.ImageFor("IS", releaseArtwork: false);
        var english = DiscordIdentity.ImageFor("IS", english: true);
        Assert.EndsWith("/is-es.jpg", spanish);
        Assert.EndsWith("/is-en.jpg", english);
        Assert.NotEqual(spanish, DiscordIdentity.ImageFor("ES"));
        using var genericCoach = AppResources.Open(DiscordIdentity.CountryArtworkResource(DiscordIdentity.ImageFor(null, coach: true))!);
        Assert.True(genericCoach.Length > 0);
        foreach (var (manifest, language) in new[] { ("country_art.json", "es"), ("country_art_en.json", "en") })
        {
            using var file = AppResources.Open(manifest);
            var images = DiscordIdentity.ReadCountryImages(file);
            Assert.Contains("IS", images.Keys);
            Assert.All(images, entry =>
            {
                var resource = DiscordIdentity.CountryArtworkResource(entry.Value);
                Assert.Equal($"country-art.{entry.Key.ToLowerInvariant()}-{language}.jpg", resource);
                using var artwork = AppResources.Open(resource!);
                Assert.True(artwork.Length > 0);
                var coachImage = DiscordIdentity.ImageFor(entry.Key, english: language == "en", coach: true);
                Assert.NotEqual(entry.Value, coachImage);
                var coachResource = DiscordIdentity.CountryArtworkResource(coachImage);
                Assert.Equal($"coach-art.{entry.Key.ToLowerInvariant()}-{language}.jpg", coachResource);
                using var coachArtwork = AppResources.Open(coachResource!);
                Assert.True(coachArtwork.Length > 0);
            });
        }
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
