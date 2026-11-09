using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class ReleaseReliabilityTests
{
    [Fact]
    public void TelemetryNeverReturnsFrameThatChangedDuringAnyOfTheThreeCopies()
    {
        var stamp = 0UL;
        var copies = 0;
        Assert.Null(TelemetryReader.ReadStableFrame(() => ++stamp, buffer =>
        {
            copies++;
            BitConverter.GetBytes(stamp).CopyTo(buffer, 24);
        }));
        Assert.Equal(3, copies);
        Assert.Null(TelemetryReader.ReadStableFrame(() => 7, buffer => BitConverter.GetBytes(8UL).CopyTo(buffer, 24)));
        Assert.NotNull(TelemetryReader.ReadStableFrame(() => 7, buffer => BitConverter.GetBytes(7UL).CopyTo(buffer, 24)));
    }

    [Fact]
    public void StartupMatchesOnlyThisExecutableAndTheMinimizedArgument()
    {
        const string path = @"C:\Apps\ETS2DiscordPresence.exe";
        Assert.True(DesktopServices.MatchesStartup($"\"{path}\" --minimized", path));
        Assert.False(DesktopServices.MatchesStartup($"\"{path}\"", path));
        Assert.False(DesktopServices.MatchesStartup("\"C:\\Old\\ETS2DiscordPresence.exe\" --minimized", path));
    }

    [Fact]
    public void UpdateChannelsSkipDraftAndOldReleasesAndKeepUnverifiedAssetsManual()
    {
        var hash = new string('a', 64);
        var releases = JsonSerializer.SerializeToElement(new[]
        {
            Release("v0.9.0", true, false, hash), Release("v0.6.0", false, false, hash),
            Release("v0.8.1", false, true, hash), Release("v0.8.0", false, false, null)
        });
        var stable = DesktopServices.SelectUpdate(releases, new Version(0, 7, 2), preview: false);
        var preview = DesktopServices.SelectUpdate(releases, new Version(0, 7, 2), preview: true);
        Assert.Equal(new Version(0, 8, 0), stable?.Version);
        Assert.Null(stable?.Sha256);
        Assert.Equal(new Version(0, 8, 1), preview?.Version);
        Assert.Equal(hash, preview?.Sha256);
        Assert.Null(DesktopServices.SelectUpdate(releases, new Version(0, 8, 1), preview: true));

        static object Release(string version, bool draft, bool prerelease, string? digest) => new
        {
            tag_name = version, draft, prerelease,
            assets = new[] { new { name = "ETS2DiscordPresence.exe", digest = digest is null ? null : $"sha256:{digest}" } }
        };
    }

    [Fact]
    public void UpdaterRejectsWrongDigestAndOversizedAssets()
    {
        using var temp = new Scratch();
        var file = Path.Combine(temp.Path, "asset");
        var bytes = Encoding.UTF8.GetBytes("test asset");
        File.WriteAllBytes(file, bytes);
        AppUpdater.VerifyFile(file, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Throws<InvalidDataException>(() => AppUpdater.VerifyFile(file, new string('0', 64)));
        Assert.Throws<InvalidDataException>(() => AppUpdater.VerifyFile(file, null));
        Assert.Throws<InvalidDataException>(() => AppUpdater.ValidateSize(AppUpdater.MaxBytes + 1));
    }

    [Fact]
    public void UpdaterWaitsForMatchingExplicitAcknowledgement()
    {
        var token = Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\ETS2DiscordPresence.Update.{token}");
        AppUpdater.ConfirmStartup(["--update-ready", Guid.NewGuid().ToString("N")]);
        Assert.False(AppUpdater.WaitForReady(ready, () => false, TimeSpan.Zero));
        AppUpdater.ConfirmStartup(["--update-ready", token]);
        Assert.True(AppUpdater.WaitForReady(ready, () => false, TimeSpan.FromSeconds(1)));
        Assert.False(AppUpdater.WaitForReady(ready, () => true, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task OverlayUsesAnotherPortAndRotationInvalidatesTheOldPrivateRoute()
    {
        var busy = new TcpListener(IPAddress.Loopback, BrowserOverlay.FirstPort) { ExclusiveAddressUse = true };
        busy.Start();
        try
        {
            using var overlay = new BrowserOverlay(Guid.NewGuid().ToString("N"));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var old = new Uri(overlay.Url);
            Assert.NotEqual(BrowserOverlay.FirstPort, old.Port);
            var health = await client.GetStringAsync(new Uri(old, "/health"));
            Assert.Contains("schemaVersion", health);
            overlay.Update("Madrid", "25 km", new AppOptions());
            Assert.Contains("Madrid", await client.GetStringAsync(old + "/state"));
            overlay.Rotate(Guid.NewGuid().ToString("N"));
            using var stale = await client.GetAsync(old + "/state");
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
            Assert.Equal(old.Port, new Uri(overlay.Url).Port);
            Assert.Contains("Madrid", await client.GetStringAsync(overlay.Url + "/state"));
        }
        finally { busy.Stop(); }
    }

    [Fact]
    public void ObsClearsLiveDataWhenPausedOrHiddenByDiscordPrivacy()
    {
        var live = new TelemetrySnapshot(false, true, "Cargo", "Madrid", "Lisboa", 0, 0, 0, 12);
        var settings = new AppSettings(Options: new AppOptions(HideDeliveryLocation: true));
        var allowed = new AppSettings();
        Assert.Equal(("", ""), PresencePrivacy.Overlay(PresencePrivacy.Apply(settings, true, false), live, true, "Idle", "25 km"));
        Assert.Equal(("", ""), PresencePrivacy.Overlay(PresencePrivacy.Apply(allowed, true, true), live, true, "Idle", "25 km"));
        Assert.Equal(("", ""), PresencePrivacy.Overlay(allowed, live, false, "Idle", "25 km"));
        Assert.Equal(("Madrid  →  Lisboa", ""), PresencePrivacy.Overlay(allowed with { ShowDistance = false }, live, true, "Idle", "25 km"));
    }

    [Fact]
    public void CatalogReportsDuplicatesAndRejectsMalformedRowsWhileIndexingValidCities()
    {
        using var stream = Bytes("""
            {"citiesList":[{"realName":"A","x":-8001,"z":0,"gameName":"same","country":"ES"},
            {"realName":"a","x":-7999,"z":3,"gameName":"SAME","country":"ES"},
            {"realName":"B","x":null},42]}
            """);
        var cities = new CityLookup(stream);
        Assert.Equal(1, cities.DuplicateIds);
        Assert.Equal(1, cities.DuplicatePositions);
        Assert.Equal(2, cities.Rejected);
        Assert.Equal(2, cities.Identified);
        Assert.Equal("A", cities.FindNearby(-8000, 0));
        Assert.Equal("ES", cities.FindCountry(-8000, 0));
    }

    [Theory]
    [InlineData("{\"ES\":\"https://example.com/1.png\",\"ES\":\"https://example.com/2.png\"}")]
    [InlineData("{\"ZZ\":\"https://example.com/1.png\"}")]
    [InlineData("{\"ES\":\"http://example.com/1.png\"}")]
    [InlineData("{\"ES\":\"invalid\"}")]
    public void RejectsInvalidCountryImageManifest(string json)
    {
        using var source = Bytes(json);
        Assert.Throws<InvalidDataException>(() => DiscordIdentity.ReadCountryImages(source));
    }

    [Fact]
    public void BundledCountryManifestIsValid()
    {
        using var source = AppResources.Open("country_art.json");
        _ = DiscordIdentity.ReadCountryImages(source);
    }

    [Fact]
    public void JournalSortsNumericColumnsAndFiltersExactTagsAndDistanceUnits()
    {
        var first = new TripEntry(Guid.NewGuid(), "1", "A", "B", "Cargo", default, DateTimeOffset.UtcNow,
            "delivered", 90, 600, Tags: "Frágil, Noche", Truck: "Volvo");
        var second = first with { Id = Guid.NewGuid(), Km = 110, DrivingSeconds = 90, Result = "cancelled" };
        Assert.Equal(new[] { first.Id, second.Id }, JournalQuery.Sort([second, first], "km", false).Select(entry => entry.Id));
        Assert.Equal(new[] { second.Id, first.Id }, JournalQuery.Sort([first, second], "time", false).Select(entry => entry.Id));
        Assert.True(JournalQuery.Matches(first, 1, "volvo", 1, false, "noche"));
        Assert.False(JournalQuery.Matches(second, 2, null, 1, false, null));
        Assert.True(JournalQuery.Matches(second, 2, null, 1, true, null));
        Assert.False(JournalQuery.Matches(first, 1, null, 0, false, "Noch"));
    }

    [Fact]
    public void VersionedBackupChecksAllEntriesBeforeReplacingFiles()
    {
        using var temp = new Scratch();
        var source = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(temp.Path, "target")).FullName;
        var backups = Path.Combine(temp.Path, "test.ets2backup");
        File.WriteAllText(Path.Combine(source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(source, "trips.json"), """{"Entries":[],"Pending":[]}""");
        File.WriteAllText(Path.Combine(target, "settings.json"), """{"Theme":"day"}""");
        LocalData.Backup(backups, source);
        var archive = JsonNode.Parse(File.ReadAllText(backups))!;
        Assert.Equal(1, archive["SchemaVersion"]!.GetValue<int>());
        archive["Files"]!["trips.json"]!["Sha256"] = new string('0', 64);
        File.WriteAllText(backups, archive.ToJsonString());
        Assert.Throws<InvalidDataException>(() => LocalData.Restore(backups, target));
        Assert.Contains("day", File.ReadAllText(Path.Combine(target, "settings.json")));
    }

    [Fact]
    public void ReplayReadsLegacyAndVersionedCompressedFilesAndReportsPartialTail()
    {
        using var temp = new Scratch();
        var path = Path.Combine(temp.Path, "recording.jsonl");
        File.WriteAllText(path, "{\"ElapsedMs\":0,\"Trip\":null}\n{\"ElapsedMs\":");
        var legacy = TelemetryReplay.ReadDetailed(path);
        Assert.True(legacy.RecoveredTail);
        Assert.Null(legacy.Metadata);
        Assert.Single(legacy.Frames);
        TelemetryReplay.Save(path + ".gz", legacy.Frames);
        var gzip = TelemetryReplay.ReadDetailed(path + ".gz");
        Assert.False(gzip.RecoveredTail);
        Assert.Equal(1, gzip.Metadata?.SchemaVersion);
        Assert.Equal(legacy.Frames, gzip.Frames);
    }

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));

    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("ets2-reliability-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(recursive: true);
    }
}
