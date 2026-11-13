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
        Assert.Equal(2, gzip.Metadata?.SchemaVersion);
        Assert.Equal(1, gzip.Metadata?.FrameCount);
        Assert.Equal(0, gzip.Metadata?.MarkersCount);
        Assert.Equal(legacy.Frames, gzip.Frames);
        using (var writer = File.CreateText(path))
        {
            TelemetryReplay.WriteHeader(writer);
            TelemetryReplay.Write(writer, legacy.Frames[0]);
        }
        Assert.Equal(1, TelemetryReplay.ReadDetailed(path).Metadata?.SchemaVersion);
    }

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));

    [Fact]
    public void TripMeasurementsExcludePausedAndTeleportedFramesAndSeparateRefueling()
    {
        using var temp = new Scratch();
        var journal = new TripJournal(Path.Combine(temp.Path, "trips.json"));
        var utc = DateTimeOffset.UnixEpoch;
        var signals = new TripSignals(100, 0, false, false, true, false, false, false, 10, 500, 200, 0, "job");
        var trip = new TelemetrySnapshot(false, true, "Boxes", "Madrid", "Lyon", 0, 0, 150_000, 12,
            Signals: signals, Truck: new TruckInfo("Renault", "T", 100, 600));
        void Observe(int sec, TelemetrySnapshot sample, string? country = "ES") =>
            journal.Observe(sample with { RenderTime = (ulong)sec + 1 }, utc.AddSeconds(sec), sec * 1000L, country);
        Observe(0, trip with { OnJob = false });
        Observe(2, trip);
        var moving = trip with { X = 60, Signals = signals with { Odometer = 500.125f }, Truck = trip.Truck! with { Litres = 99 } };
        Observe(4, moving);
        Observe(6, moving with { Paused = true });
        var still = moving with { Signals = moving.Signals! with { Speed = 0, Refueling = true }, Truck = trip.Truck! with { Litres = 200 } };
        Observe(8, still);
        Observe(10, still with { Truck = trip.Truck! with { Litres = 250 } });
        var far = still with { X = 50_000, Signals = still.Signals! with { Speed = 80, Refueling = false } };
        Observe(12, far, "FR");
        var delivered = journal.Observe(far with { OnJob = false, Signals = far.Signals! with { Delivered = true, FinishedAt = 95, DeliveredKm = 150 } },
            utc.AddSeconds(14), 14_000, "FR");
        var entry = Assert.IsType<TripEntry>(delivered);
        var measured = Assert.IsType<TripMeasurements>(entry.Measurements);
        Assert.Equal("complete", measured.Quality);
        Assert.Equal(2, measured.MovingSeconds);
        Assert.Equal(2, measured.StoppedSeconds);
        Assert.Equal(36, measured.AverageKph);
        Assert.Equal(36, measured.MaximumKph);
        Assert.Equal(1, entry.FuelLitres);
        Assert.Equal(50, measured.RefueledLitres);
        Assert.Equal(.125, measured.ObservedKm);
        Assert.Equal(150, measured.DeliveredKm);
        Assert.Equal(new[] { "ES", "FR" }, measured.Countries);
    }

    [Fact]
    public void PresenceTemplatesHonorPrivacyAndCountUtf8Bytes()
    {
        var trip = new TelemetrySnapshot(false, true, "Carga", "Lyon", "Turín", 0, 0, 1000, 12);
        var text = "{if:cargo}{cargo}{/if} {truck|Camión} {ciudaad}";
        Assert.Equal(new[] { "ciudaad" }, PresenceFormatter.UnknownVariables(text));
        Assert.Equal("Carga Camión", PresenceFormatter.Template(text, trip, new AppSettings(), null));
        Assert.Equal("Camión", PresenceFormatter.Template(text, trip, new AppSettings(ShowCargo: false), null));
        var truncated = PresenceFormatter.Limit(new string('á', 90));
        Assert.EndsWith("…", truncated);
        Assert.InRange(Encoding.UTF8.GetByteCount(truncated), 125, 128);
    }

    [Fact]
    public void PartialRestoreSnapshotUndoesNewFiles()
    {
        using var source = new Scratch();
        using var destination = new Scratch();
        var backup = Path.Combine(source.Path, "copy.ets2backup");
        var snapshot = Path.Combine(destination.Path, "undo.ets2backup");
        var settings = Path.Combine(source.Path, "settings.json");
        var trips = Path.Combine(source.Path, "trips.json");
        File.WriteAllText(settings, "{}");
        File.WriteAllText(trips, "{\"Entries\":[],\"Pending\":[]}");
        LocalData.Backup(backup, source.Path);
        LocalData.Restore(backup, destination.Path, new HashSet<string> { "trips" }, snapshot);
        Assert.False(File.Exists(Path.Combine(destination.Path, "settings.json")));
        Assert.True(File.Exists(Path.Combine(destination.Path, "trips.json")));
        Assert.Equal(1, LocalData.InspectBackup(snapshot).RemovalCount);
        LocalData.Restore(snapshot, destination.Path);
        Assert.False(File.Exists(Path.Combine(destination.Path, "trips.json")));
    }

    private sealed class Scratch : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("ets2-reliability-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(recursive: true);
    }
}
