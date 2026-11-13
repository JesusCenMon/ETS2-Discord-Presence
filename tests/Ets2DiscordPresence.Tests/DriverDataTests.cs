using System.Text;
using System.Text.Json;
using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class DriverDataTests
{
    [Theory]
    [InlineData(true, true, false, "coach")]
    [InlineData(false, false, true, "free")]
    public void ObservedDriveClosesOnceWithoutClaimingDelivery(bool coach, bool onJob, bool keepFree, string kind)
    {
        using var folder = new ScratchFolder();
        var journal = new TripJournal(Path.Combine(folder.Path, "trips.json"));
        var time = DateTimeOffset.UtcNow;
        var signals = new TripSignals(0, 0, false, false, false, false, false, false, 15, 100, 0, 0, "");
        var trip = new TelemetrySnapshot(false, onJob, "", "Lyon", "Paris", 0, 0, 0, 12, Signals: signals, Coach: coach);
        Assert.Null(journal.ObserveDrive(trip, time, 0, keepFree, true));
        Assert.Null(journal.ObserveDrive(trip with { Signals = signals with { Odometer = 100.25f } }, time.AddSeconds(2), 2000, keepFree, true));
        var result = Assert.IsType<TripEntry>(journal.ObserveDrive(null, time.AddSeconds(3), 3000, keepFree, false));
        Assert.Equal(kind, result.JourneyType);
        Assert.Equal("ended", result.Result);
        Assert.Equal(.25, result.Km);
        Assert.Null(journal.ObserveDrive(null, time.AddSeconds(4), 4000, keepFree, false));
        Assert.Single(journal.Entries);
    }

    [Fact]
    public void InterruptedRestoreRollsBackOnNextStartup()
    {
        using var folder = new ScratchFolder();
        var settings = Path.Combine(folder.Path, "settings.json");
        var backup = Path.Combine(folder.Path, "backups", "previous.ets2backup");
        File.WriteAllText(settings, """{"Language":"es"}""");
        LocalData.Backup(backup, folder.Path);
        File.WriteAllText(settings, """{"Language":"en"}""");
        File.WriteAllText(Path.Combine(folder.Path, "restore-pending.txt"), "previous.ets2backup");
        Assert.True(LocalData.RecoverInterruptedRestore(folder.Path));
        Assert.Equal("""{"Language":"es"}""", File.ReadAllText(settings));
        Assert.False(LocalData.RecoverInterruptedRestore(folder.Path));
    }

    [Fact]
    public void LegacyTripCanBeAnnotatedAndRemovedWithoutChangingAnotherDriversJournal()
    {
        using var temp = new ScratchFolder();
        var main = Path.Combine(temp.Path, "trips.json");
        var other = Path.Combine(temp.Path, Path.GetFileName(LocalData.JournalPath("Ana")));
        var id = Guid.NewGuid();
        File.WriteAllText(main, $$"""
            {"Entries":[{"Id":"{{id}}","Key":"test","Origin":"Lyon","Destination":"Turín","Cargo":"Máquinas",
            "Started":"2026-01-05T01:00:00Z","Finished":"2026-01-05T02:00:00Z","Result":"delivered","Km":20,"DrivingSeconds":300}],"Pending":[]}
            """);
        File.Copy(main, other);
        var journal = new TripJournal(main);
        Assert.Empty(Assert.Single(journal.Entries).Notes);
        Assert.True(journal.UpdateDetails(id, "Parada en ruta", "montaña", true));
        var edited = Assert.Single(new TripJournal(main).Entries);
        Assert.Equal("Parada en ruta", edited.Notes);
        Assert.Equal("montaña", edited.Tags);
        Assert.True(edited.Favorite);
        Assert.True(journal.Remove(id));
        Assert.Empty(new TripJournal(main).Entries);
        Assert.False(Assert.Single(new TripJournal(other).Entries).Favorite);
        Assert.Equal(LocalData.JournalPath("Ana"), LocalData.JournalPath("ana"));
        Assert.NotEqual(LocalData.JournalPath("Ana"), LocalData.JournalPath("Luis"));
    }

    [Fact]
    public void BackupRestoresSettingsCatalogAndJournalsAndRejectsTraversalBeforeWriting()
    {
        using var temp = new ScratchFolder();
        var source = Path.Combine(temp.Path, "source");
        var target = Path.Combine(temp.Path, "target");
        var backup = Path.Combine(temp.Path, "test.ets2backup");
        var settings = JsonSerializer.Serialize(new AppSettings(DriverProfile: "Ana", TextPercent: 135));
        var catalog = """{"citiesList":[{"realName":"A","x":12,"z":34}]}""";
        var driverFile = Path.GetFileName(LocalData.JournalPath("Ana"));
        LocalData.WriteFile(Path.Combine(source, "settings.json"), settings);
        LocalData.WriteFile(Path.Combine(source, "catalogs", "map.json"), catalog);
        LocalData.WriteFile(Path.Combine(source, driverFile), """{"Entries":[],"Pending":[]}""");
        LocalData.WriteFile(Path.Combine(source, "recordings", "ETS2-test.jsonl"), "not part of backup");
        LocalData.Backup(backup, source);
        LocalData.Restore(backup, target);
        Assert.Equal(settings, File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.Equal(catalog, File.ReadAllText(Path.Combine(target, "catalogs", "map.json")));
        Assert.True(File.Exists(Path.Combine(target, driverFile)));
        Assert.False(Directory.Exists(Path.Combine(target, "recordings")));

        File.WriteAllText(backup, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["settings.json"] = "{}", ["catalogs/../bad.json"] = "{}"
        }));
        Assert.Throws<InvalidDataException>(() => LocalData.Restore(backup, target));
        Assert.Equal(settings, File.ReadAllText(Path.Combine(target, "settings.json")));
    }

    [Fact]
    public void ReadsModelAndFuelAndIgnoresMissingCapacity()
    {
        var bytes = new byte[4400];
        bytes[0] = 1;
        BitConverter.GetBytes(12u).CopyTo(bytes, 40);
        BitConverter.GetBytes(1u).CopyTo(bytes, 52);
        BitConverter.GetBytes(150f).CopyTo(bytes, 1000);
        Encoding.UTF8.GetBytes("Volvo").CopyTo(bytes, 2364);
        Encoding.UTF8.GetBytes("FH Aero").CopyTo(bytes, 2492);
        Assert.Null(TelemetryReader.Parse(bytes)!.Truck!.FuelPercent);
        BitConverter.GetBytes(600f).CopyTo(bytes, 704);
        var truck = TelemetryReader.Parse(bytes)!.Truck!;
        Assert.Equal("Volvo", truck.Brand);
        Assert.Equal("FH Aero", truck.Model);
        Assert.Equal(25d, truck.FuelPercent);
        Assert.Null((truck with { Litres = float.NaN }).FuelPercent);
    }

    [Fact]
    public void ReplayCanSeekBackwardsAndFindPickupFerryAndDeliveryOnce()
    {
        var signal = new TripSignals(780, 0, false, false, false, false, false, false, 12, 500, 200, 0, "test");
        var trip = new TelemetrySnapshot(false, true, "Cargo", "A", "B", 0, 0, 1000, 12, Signals: signal);
        var loaded = trip with { Signals = signal with { Loaded = true } };
        var ferry = loaded with { Signals = loaded.Signals! with { Ferry = true } };
        var delivered = ferry with { OnJob = false, Signals = ferry.Signals! with { Delivered = true, FinishedAt = 700 } };
        RecordedFrame[] frames = [new(0, trip), new(1000, loaded), new(2000, ferry), new(3000, delivered), new(4000, delivered)];
        Assert.Equal(4, TelemetryReplay.IndexAt(frames, 9000));
        Assert.Equal(1, TelemetryReplay.IndexAt(frames, 1500));
        Assert.Equal(-1, TelemetryReplay.IndexAt(frames, -1));
        Assert.Equal(new[] { (1000L, "pickup"), (2000L, "ferry"), (3000L, "delivery") }, TelemetryReplay.Milestones(frames));
        Assert.Equal(new DateTime(2026, 11, 2), MainForm.StartOfWeek(new DateTime(2026, 11, 8)));
    }

    private sealed class ScratchFolder : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("ets2-local-data-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(recursive: true);
    }
}
