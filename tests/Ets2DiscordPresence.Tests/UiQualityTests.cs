using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace Ets2DiscordPresence.Tests;

public sealed class UiQualityTests
{
    [Fact]
    public void PerformanceHistoryAndStallsRemainBoundedAndSeparateColdVisits()
    {
        var metrics = new UiPerformance();
        Assert.True(metrics.Visit("journal"));
        Assert.False(metrics.Visit("journal"));
        for (var i = 0; i < 400; i++)
        {
            metrics.Record("journal", 12, 30, i == 0);
            metrics.RecordStall(i);
        }
        Assert.Equal(UiPerformance.Capacity, metrics.Samples.Count);
        Assert.Equal(32, metrics.Stalls.Count);
        Assert.Equal(100, metrics.Samples[^1].BudgetMs);
        Assert.DoesNotContain("NaN", metrics.Report());
    }

    [Fact]
    public void ScreenRecognitionRequiresConfirmationAndToleratesOneLostFrame()
    {
        var stability = new ScreenStability();
        var map = new GameScreenReader.Scan(GameScreen.Map, GameScreenReader.ScanStatus.Recognized);
        var photo = new GameScreenReader.Scan(GameScreen.Photo, GameScreenReader.ScanStatus.Recognized);
        var missing = new GameScreenReader.Scan(null, GameScreenReader.ScanStatus.NoMatch);
        Assert.Null(stability.Observe(map, 1000));
        Assert.Equal(GameScreen.Map, stability.Observe(map, 2000));
        Assert.Equal(GameScreen.Map, stability.Observe(photo, 3000));
        Assert.Equal(GameScreen.Photo, stability.Observe(photo, 4000));
        Assert.Equal(GameScreen.Photo, stability.Observe(missing, 5000));
        Assert.Null(stability.Observe(missing, 6000));
        stability.Reset();
        Assert.Null(stability.Current);
    }

    [Fact]
    public void UnavailableCaptureAndAltTabDoNotBecomePositiveMatchesOrPersistForever()
    {
        var stability = new ScreenStability();
        var map = new GameScreenReader.Scan(GameScreen.Map, GameScreenReader.ScanStatus.Recognized);
        stability.Observe(map, 1000);
        stability.Observe(map, 2000);
        Assert.Equal(GameScreen.Map, stability.Observe(new(null, GameScreenReader.ScanStatus.GameNotInForeground), 4000));
        Assert.Null(stability.Observe(new(null, GameScreenReader.ScanStatus.CaptureUnavailable), 9000));
    }

    [Theory]
    [InlineData("MAPA DEL MUNDO Exploración: 27,05%", "Map")]
    [InlineData("World map Explored 27.05%", "Map")]
    [InlineData("Tomar fotografía Ajustes del modo Foto Abrir álbum fotográfico", "Photo")]
    [InlineData("Take photo Photo mode settings Open photo album", "Photo")]
    public void SpanishAndEnglishScreenLabelsAreRecognized(string labels, string expected) =>
        Assert.Equal(expected, GameScreenReader.Classify(labels)?.ToString());

    [Theory]
    [InlineData("Photo delivery Camera cargo")]
    [InlineData("Map updates available")]
    [InlineData("Rest stop Paris Lyon")]
    public void UnrelatedTextDoesNotChangeTheActivity(string labels) => Assert.Null(GameScreenReader.Classify(labels));

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void PhotoReticleWorksAtSeveralResolutionsWithoutTextRecognition(int width, int height)
    {
        using var image = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using var drawing = Graphics.FromImage(image);
        drawing.Clear(Color.FromArgb(12, 22, 32));
        var scale = Math.Min(width / 1920d, height / 1080d);
        using var pen = new Pen(Color.White, Math.Max(1, (float)(2 * scale)));
        foreach (var (side, vertical) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var x = (float)Math.Round(width * .514 + side * 149 * scale);
            var y = (float)Math.Round(height * .490 + vertical * 102 * scale);
            drawing.DrawLine(pen, x, y, x - side * (float)(45 * scale), y);
            drawing.DrawLine(pen, x, y, x, y - vertical * (float)(45 * scale));
        }
        drawing.Flush();
        Assert.True(GameScreenReader.HasPhotoReticle(image));
        drawing.Clear(Color.White);
        drawing.Flush();
        Assert.False(GameScreenReader.HasPhotoReticle(image));
    }

    [Fact]
    public void LargeJournalQueriesUseImmutableSnapshotsAndCanBeCancelled()
    {
        var entries = Enumerable.Range(0, 50_000).Select(index => new TripEntry(Guid.NewGuid(), index.ToString(),
            "Lyon", index % 2 == 0 ? "Paris" : "Turin", "Steel", DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow, "delivered", index, 3600, Favorite: index % 2 == 0)).ToArray();
        var filter = new JournalQuery.Filter("Paris", null, null, true, 1, null, 0, false, null, "km", true);
        var result = JournalQuery.Execute(entries, filter, CancellationToken.None);
        Assert.Equal(25_000, result.Entries.Length);
        Assert.Equal(49_998, result.Entries[0].Km);
        Assert.Equal(25_000, result.Delivered);
        Assert.Equal("Lyon → Paris", result.TopRoute);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => JournalQuery.Execute(entries, filter, cancellation.Token));
    }

    [Fact]
    public async Task ViewStateSurvivesRestartAndIgnoresDamagedFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ets2-ui-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "view-state.json");
        try
        {
            var state = new UiViewState(path);
            var selected = Guid.NewGuid();
            state.Set("journal", new JournalViewState("París", true, Selected: selected, Scroll: 40));
            var first = state.SaveAsync();
            state.Set("replay", new ReplayViewState("Lyon", true, "ETS2-example.jsonl", 5));
            await state.SaveAsync();
            await first;
            var restored = new UiViewState(path);
            Assert.Equal(selected, restored.Get<JournalViewState>("journal")?.Selected);
            Assert.Equal(40, restored.Get<JournalViewState>("journal")?.Scroll);
            Assert.Equal("Lyon", restored.Get<ReplayViewState>("replay")?.Search);
            File.WriteAllText(path, "{damaged");
            Assert.Null(new UiViewState(path).Get<JournalViewState>("journal"));
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
