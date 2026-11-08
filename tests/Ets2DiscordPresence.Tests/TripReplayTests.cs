using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class TripReplayTests
{
    [Fact]
    public void ReplaysSessionWithRestartAndCompletionWithoutDuplicatingTrips()
    {
        var journalFile = Path.Combine(Path.GetTempPath(), $"ets2-journal-{Guid.NewGuid():N}.json");
        var replayFile = Path.ChangeExtension(journalFile, ".jsonl");
        try
        {
            var signals = new TripSignals(780, 0, false, false, true, false, false, false, 12, 500, 200, 0, "same-job", "torino");
            var trip = new TelemetrySnapshot(false, true, "=Maquinaria", "Lyon", "Turín", 0, 0, 80_000, 12, Signals: signals);
            var arrived = trip with { OnJob = false, Signals = signals with { FinishedAt = 750, Delivered = true, DeliveredKm = 80 } };
            using (var writer = File.CreateText(replayFile))
            {
                TelemetryReplay.Write(writer, new(0, trip));
                TelemetryReplay.Write(writer, new(2000, trip with { Signals = signals with { Odometer = 500.4f } }));
                TelemetryReplay.Write(writer, new(4000, null));
                TelemetryReplay.Write(writer, new(10000, trip));
                TelemetryReplay.Write(writer, new(12000, arrived));
                TelemetryReplay.Write(writer, new(14000, arrived));
                var next = trip with { Signals = signals with { Deadline = 1100, Delivered = true, JobKey = "second-job", FinishedAt = 750 } };
                TelemetryReplay.Write(writer, new(16000, next));
                TelemetryReplay.Write(writer, new(18000, next with
                {
                    OnJob = false, Signals = next.Signals! with { FinishedAt = 900, Cancelled = true }
                }));
            }
            var frames = TelemetryReplay.Read(replayFile);
            var clock = DateTimeOffset.UnixEpoch;
            var journal = new TripJournal(journalFile);
            foreach (var frame in frames.Take(3)) journal.Observe(frame.Trip, clock.AddMilliseconds(frame.ElapsedMs), frame.ElapsedMs);
            journal.Flush();
            journal = new TripJournal(journalFile);
            journal.Observe(frames[3].Trip, clock.AddSeconds(10), 10000);
            Assert.True(journal.Recovered);
            foreach (var frame in frames.Skip(4)) journal.Observe(frame.Trip, clock.AddMilliseconds(frame.ElapsedMs), frame.ElapsedMs);
            var entries = new TripJournal(journalFile).Entries;
            Assert.Collection(entries,
                delivered =>
                {
                    Assert.Equal("delivered", delivered.Result);
                    Assert.Equal(80, delivered.Km);
                    Assert.Equal(2, delivered.DrivingSeconds);
                },
                cancelled => Assert.Equal("cancelled", cancelled.Result));
            TripJournal.Export(replayFile, entries, false);
            Assert.Contains("\"'=Maquinaria\"", File.ReadAllText(replayFile));
        }
        finally
        {
            File.Delete(journalFile);
            File.Delete(replayFile);
        }
    }

    [Fact]
    public void ReconnectResendsLiveActivityAndKeepsReplayAndPrivateSessionsLocal()
    {
        var state = new PresencePublication();
        var snapshot = new TelemetrySnapshot(false, true, "Maquinaria", "Lyon", "Turín", 0, 0, 80_000, 12);
        var text = PresenceFormatter.Format(snapshot, "Lyon", new AppSettings(DiscordLanguage: "en", ShowCity: false,
            PresenceOrder: ["route", "cargo", "city", "distance"]));
        Assert.Equal("Lyon → Turín", text.Details);
        Assert.Contains("Delivering", text.State);
        Assert.DoesNotContain("Near", text.State);
        Assert.True(state.ShouldSend(PresencePublication.Select(text, false, true, true), 1));
        Assert.False(state.ShouldSend(text, 1));
        Assert.True(state.ShouldSend(PresencePublication.Select(text, false, true, false), 1));
        Assert.True(state.ShouldSend(text, 2));
        Assert.Null(PresencePublication.Select(text, true, true, true));
        Assert.Null(PresencePublication.Select(text, false, false, true));
    }
}
