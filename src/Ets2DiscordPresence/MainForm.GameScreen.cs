namespace Ets2DiscordPresence;

internal enum GameScreen { Photo, Map, Customize }

internal sealed partial class MainForm
{
    private FileSystemWatcher? photoWatcher;
    private long photoTakenAt = -1;
    private GameScreen? selectedGameScreen;
    private long selectedScreenUntil;
    private bool lastVehicleWasCoach;
    private GameScreen? recognizedScreen;
    private GameScreen? screenCandidate;
    private int candidateSamples;
    private bool screenScanPending;
    private int screenScanGeneration;

    private void AddGameScreenMenu()
    {
        var menu = new ToolStripMenuItem();
        var automatic = new ToolStripMenuItem();
        automatic.Click += (_, _) => SetGameScreen(null);
        menu.DropDownItems.Add(automatic);
        foreach (var screen in Enum.GetValues<GameScreen>())
        {
            var item = new ToolStripMenuItem();
            item.Click += (_, _) => SetGameScreen(screen);
            menu.DropDownItems.Add(item);
            menu.DropDownOpening += (_, _) =>
            {
                item.Text = PresenceFormatter.ScreenTitle(screen, lastVehicleWasCoach, English);
                item.Checked = selectedGameScreen == screen;
            };
        }
        trayMenu.Opening += (_, _) =>
        {
            menu.Text = L("Actividad de ETS2", "ETS2 activity");
            menu.Enabled = IsGameRunning();
            automatic.Text = L("Automática", "Automatic");
            automatic.Checked = selectedGameScreen is null;
        };
        trayMenu.Items.Add(menu);
    }

    private void SetGameScreen(GameScreen? screen)
    {
        selectedGameScreen = screen;
        selectedScreenUntil = Environment.TickCount64 + 10 * 60_000;
        publishImmediately = true;
        RefreshPresence();
    }

    private GameScreen? DetectGameScreen(bool running, TelemetrySnapshot? telemetry, long now)
    {
        if (!running)
        {
            ResetScreenRecognition();
            selectedGameScreen = null;
            lastVehicleWasCoach = false;
            Interlocked.Exchange(ref photoTakenAt, -1);
            return null;
        }
        if (telemetry is not null) lastVehicleWasCoach = telemetry.Coach;
        var mapEnabled = preferences.Features.RecognizeMap ?? preferences.Features.RecognizeGameScreens;
        var photoEnabled = preferences.Features.RecognizePhoto ?? preferences.Features.RecognizeGameScreens;
        if (telemetry?.Paused == false || !mapEnabled && !photoEnabled
            || !preferences.Sharing || privateForSession || privateUntil > DateTimeOffset.UtcNow
            || demo || replayOpen || demoScenario is not null)
            ResetScreenRecognition();
        else if (selectedGameScreen is null && !screenScanPending && GameScreenReader.HasRecognitionLanguage)
            _ = ReadGameScreenAsync(screenScanGeneration);
        if (selectedGameScreen is { } chosen)
        {
            if (now < selectedScreenUntil && telemetry?.Paused != false) return chosen;
            selectedGameScreen = null;
        }
        if (recognizedScreen == GameScreen.Map && mapEnabled || recognizedScreen == GameScreen.Photo && photoEnabled)
            return recognizedScreen;
        var photo = Interlocked.Read(ref photoTakenAt);
        return photoEnabled && photo >= 0 && now - photo is >= 0 and < 12_000 && telemetry?.Paused != false
            ? GameScreen.Photo : null;
    }

    private void ResetScreenRecognition()
    {
        screenScanGeneration++;
        recognizedScreen = screenCandidate = null;
        candidateSamples = 0;
    }

    private async Task ReadGameScreenAsync(int generation)
    {
        screenScanPending = true;
        try
        {
            var result = await GameScreenReader.ReadAsync();
            if (IsDisposed || generation != screenScanGeneration) return;
            candidateSamples = result == screenCandidate ? candidateSamples + 1 : 1;
            screenCandidate = result;
            if (candidateSamples >= 2) recognizedScreen = result;
        }
        finally { screenScanPending = false; }
    }

    private void WatchGamePhotos()
    {
        if (photoWatcher is not null) return;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Euro Truck Simulator 2", "screenshot");
        if (!Directory.Exists(folder)) return;
        try
        {
            var watcher = new FileSystemWatcher(folder, "*.png") { NotifyFilter = NotifyFilters.FileName };
            watcher.Created += OnGamePhoto;
            watcher.Renamed += OnGamePhoto;
            try { watcher.EnableRaisingEvents = true; }
            catch { watcher.Dispose(); throw; }
            photoWatcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void OnGamePhoto(object sender, FileSystemEventArgs args) =>
        Interlocked.Exchange(ref photoTakenAt, Environment.TickCount64);
}
