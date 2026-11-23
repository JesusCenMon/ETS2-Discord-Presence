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
    private readonly ScreenStability screenStability = new();
    private DateTimeOffset? lastScreenScanAt;
    private double lastScreenScanMs;
    private CancellationTokenSource? screenCancellation;
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
        else if (selectedGameScreen is null && !screenScanPending && now >= nextScreenScan)
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
        recognizedScreen = null;
        screenStability.Reset();
        screenCancellation?.Cancel();
        lastGameScreenMatch = 0;
    }

    private async Task ReadGameScreenAsync(int generation)
    {
        screenScanPending = true;
        try
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            screenCancellation = cancel;
            for (var pass = 0; pass < 2; pass++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var result = await GameScreenReader.ReadAsync(cancel.Token);
                if (IsDisposed || cancel.IsCancellationRequested || generation != screenScanGeneration) return;
                lastScreenScanAt = DateTimeOffset.Now;
                lastScreenScanMs = clock.Elapsed.TotalMilliseconds;
                lastScreenScan = result.Status;
                var previousScreen = recognizedScreen;
                recognizedScreen = screenStability.Observe(result, Environment.TickCount64);
                if (recognizedScreen.HasValue) lastGameScreenMatch = Environment.TickCount64;
                nextScreenScan = Environment.TickCount64 + (result.Status == GameScreenReader.ScanStatus.GameNotInForeground ? 3500 : 1000);
                if (previousScreen != recognizedScreen)
                {
                    publishImmediately = true;
                    BeginInvoke(RefreshPresence);
                }
                // Confirm a positive candidate promptly instead of waiting for the next telemetry tick.
                if (pass == 0 && result.Screen.HasValue && recognizedScreen != result.Screen)
                    await Task.Delay(350, cancel.Token);
                else break;
            }
        }
        catch (OperationCanceledException) { }
        finally { screenScanPending = false; screenCancellation = null; }
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

    private string ScreenDiagnostic() => selectedGameScreen is { } manual
        ? L("Actividad elegida manualmente: ", "Manually selected activity: ") + PresenceFormatter.ScreenTitle(manual, lastVehicleWasCoach, English)
        : demo || demoScenario is not null || replayOpen
        ? L("La detección está detenida durante ejemplos y repeticiones locales.", "Detection is stopped during local demos and replays.")
        : !(preferences.Features.RecognizeMap ?? preferences.Features.RecognizeGameScreens)
        && !(preferences.Features.RecognizePhoto ?? preferences.Features.RecognizeGameScreens)
        ? L("Detección desactivada en Ajustes.", "Detection disabled in Settings.")
        : !preferences.Sharing || privateForSession || privateUntil > DateTimeOffset.UtcNow
        ? L("Detección detenida mientras la publicación está pausada u oculta.", "Detection stopped while sharing is paused or hidden.")
        : !gameRunning ? L("Abre ETS2 para comprobar el detector.", "Launch ETS2 to check the detector.")
        : !GameScreenReader.HasRecognitionLanguage
        ? L("Windows no tiene reconocimiento de texto disponible. El modo foto aún puede reconocerse por sus controles visuales.", "Windows has no text recognition language. Photo mode can still be recognized by its visual controls.")
        : lastScreenScan switch
        {
            GameScreenReader.ScanStatus.Recognized => recognizedScreen is { } screen
                ? PresenceFormatter.ScreenTitle(screen, lastVehicleWasCoach, English)
                : L("Confirmando la pantalla. Mantén el mapa o el modo foto abierto un momento.", "Confirming the screen. Keep map or photo mode open briefly."),
            GameScreenReader.ScanStatus.NoMatch => L("Ventana leída sin coincidencias. Mantén visibles el encabezado del mapa o los controles del modo foto.", "Window read without a match. Keep the map header or photo-mode controls visible."),
            GameScreenReader.ScanStatus.CaptureUnavailable => L("No se pudo leer la imagen de ETS2. Prueba el modo ventana sin bordes.", "Could not read the ETS2 image. Try borderless windowed mode."),
            _ => L("Pon ETS2 en primer plano para iniciar la detección.", "Bring ETS2 to the foreground to start detection.")
        };
}
