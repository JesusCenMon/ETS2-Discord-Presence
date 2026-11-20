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
    private long nextScreenScan;
    private GameScreenReader.ScanStatus? lastScreenScan;
    private bool screenScanPending;
    private int screenScanGeneration;
    private long lastGameScreenMatch;

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
        if (!mapEnabled && !photoEnabled
            || !preferences.Sharing || privateForSession || privateUntil > DateTimeOffset.UtcNow
            || demo || replayOpen || demoScenario is not null)
            ResetScreenRecognition();
        else if (selectedGameScreen is null && !screenScanPending && now >= nextScreenScan && GameScreenReader.HasRecognitionLanguage)
        {
            nextScreenScan = now + 1000;
            _ = ReadGameScreenAsync(screenScanGeneration);
        }
        if (selectedGameScreen is { } chosen)
        {
            if (now < selectedScreenUntil && (telemetry?.Paused != false || telemetry.Signals is not { Speed: var speed } || Math.Abs(speed) < 1)) return chosen;
            selectedGameScreen = null;
        }
        if (now - lastGameScreenMatch <= 6500 &&
            (recognizedScreen == GameScreen.Map && mapEnabled || recognizedScreen == GameScreen.Photo && photoEnabled))
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
        lastGameScreenMatch = 0;
    }

    private async Task ReadGameScreenAsync(int generation)
    {
        screenScanPending = true;
        try
        {
            var result = await GameScreenReader.ReadAsync();
            if (IsDisposed || generation != screenScanGeneration) return;
            lastScreenScan = result.Status;
            var detected = result.Screen;
            var previousScreen = recognizedScreen;
            if (detected.HasValue) lastGameScreenMatch = Environment.TickCount64;
            candidateSamples = detected == screenCandidate ? Math.Min(2, candidateSamples + 1) : 1;
            screenCandidate = detected;
            // Positive matches include multiple screen-specific labels. Losing a match
            // twice prevents a transient overlay from interrupting the activity.
            if (result.Status != GameScreenReader.ScanStatus.GameNotInForeground
                && (detected.HasValue || candidateSamples >= 2 || liveSnapshot?.Paused == false))
                recognizedScreen = detected;
            if (previousScreen != recognizedScreen)
            {
                publishImmediately = true;
                BeginInvoke(RefreshPresence);
            }
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
            var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName };
            watcher.Created += OnGamePhoto;
            watcher.Renamed += OnGamePhoto;
            try { watcher.EnableRaisingEvents = true; }
            catch { watcher.Dispose(); throw; }
            photoWatcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void OnGamePhoto(object sender, FileSystemEventArgs args)
    {
        if (Path.GetExtension(args.Name)?.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
            Interlocked.Exchange(ref photoTakenAt, Environment.TickCount64);
    }

    private string ScreenDiagnostic() => !GameScreenReader.HasRecognitionLanguage
        ? L("Windows no tiene un idioma de reconocimiento instalado.", "Windows has no recognition language installed.")
        : lastScreenScan switch
        {
            GameScreenReader.ScanStatus.Recognized => L("Pantalla de ETS2 reconocida.", "ETS2 screen recognized."),
            GameScreenReader.ScanStatus.NoMatch => L("Ventana leída. No se reconoció mapa ni modo foto.", "Window read. Map or photo mode was not recognized."),
            GameScreenReader.ScanStatus.CaptureUnavailable => L("No se pudo leer la imagen de ETS2. Prueba el modo ventana sin bordes.", "Could not read the ETS2 image. Try borderless windowed mode."),
            _ => L("Pon ETS2 en primer plano para iniciar la detección.", "Bring ETS2 to the foreground to start detection.")
        };
}
