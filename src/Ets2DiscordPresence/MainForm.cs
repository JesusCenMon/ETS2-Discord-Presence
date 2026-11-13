using System.ComponentModel;
using System.Diagnostics;
using DiscordRPC;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm : Form
{
    private static readonly TelemetrySnapshot Sample = new(false, true, "Maquinaria industrial para obras de infraestructura",
        "Lyon", "Turín", -21_500, 27_000, 215_000, 12, 1, 9 * 60 + 30, 10_800, 390,
        new TripSignals(770, 0, false, false, true, false, false, false, 15, 500, 200, 0, "example", "torino"),
        new TruckInfo("Renault", "T High", 425, 700));

    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly DiscordRpcClient discord = new(DiscordIdentity.ApplicationId);
    private readonly CancellationTokenSource lifetime = new();
    private CityLookup cities;
    private TripJournal journal;
    private TripEntry? completedTrip;
    private static bool isLight => Application.ColorMode != SystemColorMode.Dark
        && (Application.ColorMode != SystemColorMode.System || Application.SystemColorMode != SystemColorMode.Dark)
        || SystemInformation.HighContrast;
    private readonly Icon appIcon;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip trayMenu = new();
    private readonly bool startMinimized;
    private readonly ToolStripMenuItem trayPause = new();
    private AppSettings preferences = AppSettings.Load();
    private readonly string initialTheme;
    private readonly PresencePublication publication = new();
    private readonly System.Windows.Forms.Timer presenceTimer = new() { Interval = 500 };
    private PresenceText? pendingPresence;
    private bool publishImmediately;
    private StreamWriter? recording;
    private long recordingStarted;
    private DateTime? activityStarted;
    private string? activityKey;
    private volatile bool discordReady;
    private volatile bool discordWasReady;
    private int connectionGeneration;
    private bool demo;
    private bool restartNeeded;
    private bool gamePrepared;
    private bool canLocateGame;
    private string? setupError;
    private string? settingsError;
    private string? gamePath;
    private AppUpdate? availableUpdate;
    private long nextGameCheck;
    private ulong? lastFrame;
    private long lastFrameAt;
    private string? city;
    private string? cityCandidate;
    private int citySamples;
    private string? countryCandidate;
    private string? currentCountry;
    private int countrySamples;
    private uint? lastRevision;

    public MainForm(bool startMinimized = false)
    {
        initialTheme = preferences.Theme;
        this.startMinimized = startMinimized;
        journal = new TripJournal(LocalData.JournalPath(preferences.DriverProfile));
        Text = "ETS2 Discord Presence";
        ClientSize = new Size(1160, 760);
        MinimumSize = new Size(420, 410);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        using var iconData = AppResources.Open("ets2-presence.ico");
        appIcon = new Icon(iconData);
        Icon = appIcon;
        using var cityData = AppResources.Open("cities_default.json");
        cities = new CityLookup(cityData);
        LoadCatalog();
        BuildLayout();

        tray = new NotifyIcon { Icon = appIcon, Text = Text, Visible = true, ContextMenuStrip = trayMenu };
        trayMenu.Items.Add("Abrir panel", null, (_, _) => RestoreWindow());
        trayPause.Click += (_, _) => ToggleSharing();
        trayMenu.Items.Add(trayPause);
        var discordModes = new ToolStripMenuItem("Perfil de Discord");
        foreach (var (key, spanish, english) in new[] { ("normal", "Normal", "Normal"), ("stream", "Stream", "Stream"), ("private", "Privado", "Private") })
        {
            var item = new ToolStripMenuItem(spanish);
            item.Click += (_, _) => SwitchDiscordProfile(key);
            trayMenu.Opening += (_, _) => { item.Text = L(spanish, english); item.Checked = preferences.Features.DiscordMode == key; };
            discordModes.DropDownItems.Add(item);
        }
        trayMenu.Opening += (_, _) => discordModes.Text = L("Perfil de Discord", "Discord profile");
        trayMenu.Items.Add(discordModes);
        AddGameScreenMenu();
        trayQuick.Click += (_, _) => ShowTrayQuickPanel();
        trayMenu.Items.Add("Licencias", null, (_, _) => ShowLicenses());
        trayMenu.Items.Add("Salir", null, (_, _) => Close());
        trayMenu.Items.Insert(1, trayQuick);
        tray.DoubleClick += (_, _) => RestoreWindow();
        UpdateSharingControls();

        if (startMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && Visible) Hide();
            ArrangeLayout();
        };
        DpiChanged += (_, _) => { ResizeShell(); ArrangeLayout(); };
        timer.Tick += (_, _) => RefreshPresence();
        presenceTimer.Tick += (_, _) =>
        {
            presenceTimer.Stop();
            var presence = pendingPresence;
            pendingPresence = null;
            if (!discordReady) return;
            Publish(presence);
            RenderPreview(lastSent, null);
        };
        toggle.Click += (_, _) => ToggleSharing();
        locate.Click += (_, _) => LocateGame();
        launch.Click += (_, _) => OpenExternal("steam://rungameid/227300");
        ApplyCompactSize();
        ApplyLanguage();
        ConfigurePanels();
        Restyle(this);
        ScaleText(this);
        RememberWindow(this, "main");
        Shown += (_, _) => Start();
        FormClosed += (_, _) =>
        {
            lifetime.Cancel();
            timer.Dispose();
            presenceTimer.Dispose();
            photoWatcher?.Dispose();
            discord.Dispose();
            tray.Dispose();
            trayMenu.Dispose();
            var closingDrive = journal.ObserveDrive(null, DateTimeOffset.UtcNow, Environment.TickCount64, false, false);
            if (closingDrive?.JourneyType == "coach" && recordedJob?.StartsWith("coach|", StringComparison.Ordinal) == true
                && recordingPath is { } closingPath && recordingFrames > 0)
                journal.AttachRecording(closingDrive.Id, Path.GetFileName(closingPath));
            journal.Flush();
            overlay?.Dispose();
            FinishRecording();
            captureWindow?.Close();
            quickWindow?.Close();
            appIcon.Dispose();
            lifetime.Dispose();
        };
    }

    private async void Start()
    {
        if (startMinimized) Hide();
        if (AppSettings.RecoveredSettings)
            settingsError = L("Se recuperó la última copia válida de los ajustes.", "Recovered the last valid copy of your settings.");
        AppUpdater.CleanWorker(Application.ExecutablePath);
        if (AppUpdater.ConsumeFailure(Application.ExecutablePath) is { } failure)
            settingsError = L("La actualización no se pudo completar.", "The update could not be completed.")
                + (failure.Length == 0 ? "" : $" {failure}");
        discord.OnReady += (_, _) =>
        {
            discordWasReady = true;
            Interlocked.Exchange(ref lastDiscordConnection, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            lastDiscordError = null;
            Interlocked.Increment(ref connectionGeneration);
            discordReady = true;
        };
        discord.OnPresenceUpdate += (_, message) =>
        {
            var sent = lastSent;
            discordConfirmed = discordReady && sent is not null && message.Presence is { } ack
                && ack.Details == sent.Details && ack.State == sent.State;
        };
        discord.OnClose += (_, _) => { discordReady = false; discordConfirmed = false; };
        discord.OnConnectionFailed += (_, _) => { discordReady = false; discordConfirmed = false; lastDiscordError = "connection_failed"; };
        discord.Initialize();
        DiscoverGame();
        RefreshPresence();
        timer.Start();
        _ = CheckReleaseArtworkAsync();
        if (!retentionChecked)
        {
            retentionChecked = true;
            try { LocalData.PruneRecordings(preferences.RetentionDays, recordingPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }

        await RefreshUpdateAsync();
    }

    private async Task RefreshUpdateAsync()
    {
        try
        {
            var preview = preferences.PreviewUpdates;
            var release = await DesktopServices.CheckForUpdateAsync(preview, lifetime.Token);
            if (!IsDisposed && preview == preferences.PreviewUpdates) availableUpdate = release;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // An unavailable update check does not affect the trip or Discord.
        }
    }

    private void DiscoverGame()
    {
        nextGameCheck = Environment.TickCount64 + 15_000;
        try
        {
            var detected = SteamLocator.IsValidGamePath(preferences.GamePath)
                ? preferences.GamePath : SteamLocator.FindGame();
            if (detected != gamePath || (!gamePrepared && setupError is null)) PrepareGame(detected);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetSetupError(L("No se pudo leer la biblioteca de Steam. Usa Buscar ETS2.", "The Steam library could not be read. Use Locate ETS2."), allowBrowse: true);
        }
    }

    private void PrepareGame(string? path)
    {
        gamePath = path;
        setupError = null;
        if (preferences.ReaderDisabled)
        {
            gamePrepared = true;
            canLocateGame = path is null;
            return;
        }
        canLocateGame = false;
        if (path is null)
        {
            SetSetupError(L("ETS2 no aparece en Steam. Selecciona su carpeta.", "ETS2 was not found in Steam. Select its folder."), allowBrowse: true);
            return;
        }
        try
        {
            restartNeeded = PluginInstaller.Prepare(path) && IsGameRunning();
            gamePrepared = true;
        }
        catch (UnauthorizedAccessException)
        {
            SetSetupError(L("Windows protegió la carpeta de ETS2. Abre esta app como administrador para prepararla.", "Windows protected the ETS2 folder. Run this app as administrator to prepare it."), allowBrowse: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
        {
            SetSetupError(ex.Message, allowBrowse: !SteamLocator.IsValidGamePath(path));
        }
    }

    private void SetSetupError(string message, bool allowBrowse)
    {
        gamePrepared = false;
        setupError = message;
        canLocateGame = allowBrowse;
    }

    private void LocateGame()
    {
        using var picker = new FolderBrowserDialog { Description = L("Selecciona la carpeta de Euro Truck Simulator 2", "Select the Euro Truck Simulator 2 folder") };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        PrepareGame(picker.SelectedPath);
        if (gamePrepared) SavePreferences(preferences with { GamePath = picker.SelectedPath });
        RefreshPresence();
    }

    private void SavePreferences(AppSettings updated)
    {
        try
        {
            updated.Save();
            if (!string.Equals(preferences.DriverProfile, updated.DriverProfile, StringComparison.OrdinalIgnoreCase))
            {
                journal.ObserveDrive(null, DateTimeOffset.UtcNow, Environment.TickCount64, false, false);
                journal.Flush();
                journal = new TripJournal(LocalData.JournalPath(updated.DriverProfile));
                completedTrip = null;
            }
            var themeChanged = preferences.Theme != updated.Theme;
            var channelChanged = preferences.PreviewUpdates != updated.PreviewUpdates;
            publishImmediately = true;
            preferences = updated;
            if (channelChanged)
            {
                availableUpdate = null;
                _ = RefreshUpdateAsync();
            }
            if (themeChanged) ApplyShellTheme();
            settingsError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!updated.Sharing && preferences.Sharing)
            {
                preferences = preferences with { Sharing = false };
                publishImmediately = true;
                settingsError = L("Publicación pausada. Windows impidió guardar la pausa para el próximo inicio.",
                    "Publishing paused. Windows could not save the pause for next startup.");
            }
            else settingsError = L("Windows no permitió guardar tus preferencias.", "Windows did not allow saving your settings.");
        }
        RefreshPresence();
    }

    private void SwitchDiscordProfile(string key)
    {
        if (key == preferences.Features.DiscordMode) return;
        var profiles = new Dictionary<string, DiscordProfile>(preferences.Features.DiscordProfiles ?? []);
        var current = DiscordProfile.Capture(preferences);
        profiles[preferences.Features.DiscordMode] = current;
        var selected = profiles.GetValueOrDefault(key) ?? current;
        var updated = selected.Apply(preferences);
        SavePreferences(updated with { Options = updated.Features with { DiscordMode = key, DiscordProfiles = profiles } });
    }

    private void ToggleSharing()
    {
        SavePreferences(preferences with { Sharing = !preferences.Sharing });
        UpdateSharingControls();
    }

    private void UpdateSharingControls()
    {
        var action = preferences.Sharing ? L("Pausar publicación", "Pause publishing") : L("Reanudar publicación", "Resume publishing");
        toggle.Text = $"{(preferences.Sharing ? "Ⅱ" : "▶")}  {action}";
        trayPause.Text = action;
    }

    private void RefreshPresence()
    {
        var ready = discordReady && demoScenario != "offline";
        SetStatus(discordStatus, ready ? L("Discord · Conectado", "Discord · Connected") :
            discordWasReady ? L("Discord · Reconectando…", "Discord · Reconnecting…") : L("Discord · Esperando la app de escritorio", "Discord · Waiting for desktop app"), ready);

        var now = Environment.TickCount64;
        var running = IsGameRunning();
        gameRunning = running;
        if (running) WatchGamePhotos();
        if (running) sessionStarted ??= DateTime.UtcNow;
        else { sessionStarted = null; privateForSession = false; }
        if (!preferences.Features.ObsBrowser && overlay is not null)
        {
            overlay.Dispose();
            overlay = null;
        }
        try { StartOverlay(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException) { }
        RunScheduledBackup();
        if (!running) restartNeeded = false;
        if (now >= nextGameCheck && (!gamePrepared || !SteamLocator.IsValidGamePath(gamePath))) DiscoverGame();

        TelemetrySnapshot? telemetry = null;
        string? readError = null;
        if (running && !preferences.ReaderDisabled)
        {
            TelemetryReader.TryRead(out telemetry, out readError, out var detectedRevision);
            if (detectedRevision.HasValue) lastRevision = detectedRevision;
        }
        else lastRevision = null;
        readerError = readError;
        var frozen = telemetry is not null && !IsFresh(telemetry, now);
        if (frozen) telemetry = null;
        if (!running || frozen || telemetry is null && now - lastGoodTelemetryAt > 4500) ResetLocation(running);
        if (telemetry is not null)
        {
            lastGoodTelemetryAt = now;
            lastRevision = telemetry.PluginRevision;
        }

        liveSnapshot = telemetry;
        var gameScreen = DetectGameScreen(running, telemetry, now);
        locate.Visible = canLocateGame && telemetry is null;
        launch.Visible = !running;
        var status = telemetry is not null ? L("ETS2 · Viaje detectado", "ETS2 · Trip detected") : frozen ? L("ETS2 · Datos detenidos", "ETS2 · Data stopped")
            : setupError is not null ? L("ETS2 · Revisar preparación", "ETS2 · Check setup")
            : running ? L("ETS2 · Esperando la partida", "ETS2 · Waiting for game session") : L("ETS2 · Cerrado", "ETS2 · Closed");
        SetStatus(gameStatus, status, telemetry is not null);

        var nearby = telemetry is null ? null : TrackCity(cities.FindNearby(telemetry.X, telemetry.Z));
        if (telemetry is not null) TrackCountry(cities.FindCountry(telemetry.X, telemetry.Z), now);
        var ending = journal.Observe(telemetry?.Coach == true ? null : telemetry, DateTimeOffset.UtcNow, now, countrySamples >= 3 ? currentCountry : null);
        var sessionEnding = journal.ObserveDrive(telemetry, DateTimeOffset.UtcNow, now, preferences.Features.JournalFreeDrive, running);
        UpdateLiveNotices(telemetry, discordReady);
        UpdateRecording(telemetry, ending ?? (sessionEnding?.JourneyType == "coach" ? sessionEnding : null), now);
        if (ending is not null) completedTrip = ending;
        else if (telemetry?.OnJob == true) completedTrip = null;
        var simulated = demo || replayOpen || demoScenario is not null;
        var trip = demoScenario switch
        {
            "pause" => Sample with { Paused = true },
            "roam" => Sample with { OnJob = false, Cargo = "", SourceCity = "", DestinationCity = "",
                X = -65398, Z = 48701, RouteDistanceMeters = 0 },
            "offline" => null,
            "job" => Sample,
            "coach" => Sample with { Coach = true, Cargo = "", SourceCity = "Lyon", DestinationCity = "París",
                Truck = new TruckInfo("Volvo", "9700 DD", 350, 600) },
            _ => demo ? Sample : telemetry
        };
        var isExample = demo || demoScenario is not null;
        var displayedCity = demoScenario == "roam" ? "Madrid" : isExample ? "Chambéry" : nearby;
        var presenceSettings = PresencePrivacy.Apply(preferences, trip?.OnJob, privateDestination);
        var sharing = preferences.Sharing && !privateForSession && !(privateUntil > DateTimeOffset.UtcNow);
        var country = demoScenario == "roam" ? "ES" : isExample ? "FR" : currentCountry;
        homeArtworkNote = !presenceSettings.SharesCountry ? L("Portada genérica · Ubicación oculta", "Generic artwork · Location hidden")
            : country is null ? L("Portada genérica · País sin detectar", "Generic artwork · Country unavailable") : null;
        var discordCity = preferences.Features.DiscordMode == "stream" ? null : displayedCity;
        var discordEnglish = presenceSettings.DiscordLanguage == "en";
        var coachArtwork = trip?.Coach == true || (!simulated && trip is null && gameScreen.HasValue && lastVehicleWasCoach);
        var presence = !simulated && gameScreen is { } screen
            ? new PresenceText(PresenceFormatter.ScreenTitle(screen, coachArtwork, discordEnglish), DiscordIdentity.ActivityName)
            : trip is null ? null : PresenceFormatter.Format(trip, discordCity, presenceSettings, country);
        UpdateActivity(telemetry);
        if (presence is not null)
            presence = presence with
            {
                Started = preferences.ActivityTimer && preferences.Features.DiscordMode != "private"
                    ? isExample ? DateTime.UtcNow.AddMinutes(-3) : preferences.Features.SessionTimer ? sessionStarted : activityStarted : null,
                Image = preferences.ActivityImage && preferences.Features.DiscordMode != "private"
                    ? DiscordIdentity.ImageFor(presenceSettings.SharesCountry ? country : null, releaseArtwork, discordEnglish, coachArtwork) : null
            };
        RenderTrip(trip, displayedCity, presence);
        if (!simulated && gameScreen is { } displayedScreen)
            travelMode.Text = PresenceFormatter.ScreenTitle(displayedScreen, coachArtwork, English);
        RenderExtras(trip, now, running, ready);
        RenderTools(trip, telemetry, ready, sharing && !simulated);
        RenderDashboard(trip, running, ready, now);
        var boundary = !isExample && journal.BoundaryCountry is { } crossed ? $"border:{crossed}" : null;
        tripMap.UpdateMap(trip, cities, English, shellPalette.Light, isExample, !preferences.Features.LowPower || Visible, boundary);
        guidance.Text = settingsError ?? (journal.Error is not null ? L("No se pudo acceder al historial local.", "The local trip history is unavailable.") : null) ?? (replayOpen ? L("Reproducción local. No se publica en Discord.", "Local replay. Discord publishing is paused.")
            : demo ? L("Demostración local. El viaje de ejemplo no se publica.", "Local demo. The sample trip stays local.")
            : !preferences.Sharing ? L("La publicación está pausada. Puedes reanudarla desde este panel o la bandeja.", "Publishing is paused. Resume here or from the tray.")
            : telemetry is not null ? (ready ? L("Actividad preparada para Discord.", "Activity ready for Discord.") : L("Abre Discord de escritorio para compartir tu viaje.", "Open Discord desktop to share your trip."))
            : preferences.ReaderDisabled ? L("Lector desactivado. Puedes restaurarlo en Ajustes.", "Reader disabled. Restore it in Settings.")
            : setupError ?? (restartNeeded ? L("Reinicia ETS2 para activar la lectura del viaje.", "Restart ETS2 to enable trip data.")
                : frozen ? L("El juego dejó de enviar datos. Se retiró la actividad anterior.", "The game stopped sending data. The previous activity was cleared.")
                : running ? (English ? L("Entra a una partida.", "Enter a game session or check the reader in Settings.") : readError ?? "Entra a una partida.") : L("Abre ETS2 para empezar.", "Launch ETS2 to begin.")));
        if (!sharing && preferences.Sharing)
            guidance.Text = L("La actividad está oculta temporalmente.", "Activity is temporarily hidden.");
        else if (demoScenario is not null)
            guidance.Text = L("Prueba local. No se publica en Discord.", "Local test. Nothing is published to Discord.");
        else if (routeChangedUntil > now && trip?.OnJob == true)
            guidance.Text = L("La distancia o la llegada cambiaron. ETS2 pudo recalcular la ruta.",
                "Distance or arrival time changed. ETS2 may have recalculated the route.");
        else if (quotaWarning is not null && recording is not null) guidance.Text = quotaWarning;
        var outgoing = PresencePublication.Select(presence, simulated, sharing, discordReady);
        var transientReadFailure = running && telemetry is null && !frozen && now - lastGoodTelemetryAt <= 4500
            && !simulated && sharing && discordReady && !preferences.ReaderDisabled && lastSent is not null && !publishImmediately;
        if (outgoing is null && transientReadFailure)
        {
            presenceTimer.Stop();
            pendingPresence = null;
            RenderPreview(lastSent, presence);
            return;
        }
        if (outgoing is null || publishImmediately)
        {
            presenceTimer.Stop();
            pendingPresence = null;
            Publish(outgoing);
        }
        else if (outgoing != pendingPresence)
        {
            pendingPresence = outgoing;
            presenceTimer.Start();
        }
        publishImmediately = false;
        RenderPreview(outgoing is null ? null : lastSent, presence);
    }

    private long lastGoodTelemetryAt;
    private long lastKnownCountryAt;

    private bool IsFresh(TelemetrySnapshot snapshot, long now)
    {
        if (snapshot.RenderTime != lastFrame)
        {
            lastFrame = snapshot.RenderTime;
            lastFrameAt = now;
        }
        return now - lastFrameAt <= 12_000;
    }

    private string? TrackCity(string? nearby)
    {
        if (nearby != cityCandidate)
        {
            cityCandidate = nearby;
            citySamples = 1;
        }
        else if (citySamples < 3) citySamples++;
        if (citySamples == 3) city = cityCandidate;
        return city;
    }

    private void TrackCountry(string? country, long now)
    {
        if (country == currentCountry && country is not null) lastKnownCountryAt = now;
        if (country is null && currentCountry is not null && now - lastKnownCountryAt < 10_000)
        {
            countryCandidate = null;
            countrySamples = 0;
            return;
        }
        if (country != countryCandidate)
        {
            countryCandidate = country;
            countrySamples = 1;
        }
        else if (countrySamples < 3) countrySamples++;
        if (countrySamples == 3 && currentCountry != countryCandidate)
        {
            currentCountry = countryCandidate;
            lastKnownCountryAt = now;
        }
    }

    private void ResetLocation(bool running)
    {
        city = null;
        cityCandidate = null;
        citySamples = countrySamples = 0;
        currentCountry = countryCandidate = null;
        lastKnownCountryAt = 0;
        if (!running) lastFrame = null;
    }

    private void Publish(PresenceText? presence)
    {
        if (!publication.ShouldSend(presence, Volatile.Read(ref connectionGeneration))) return;
        if (presence is not null) Interlocked.Exchange(ref lastDiscordPublication, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        lastSent = presence;
        discordConfirmed = false;
        if (presence is null) discord.ClearPresence();
        else discord.SetPresence(new RichPresence
        {
            Details = presence.Details, State = presence.State,
            Assets = presence.Image is { } image ? new Assets
            {
                LargeImageKey = image, LargeImageText = DiscordIdentity.ActivityName
            } : null,
            Timestamps = presence.Started is { } start ? new Timestamps { Start = start } : null
        });
    }

    private void UpdateActivity(TelemetrySnapshot? trip)
    {
        var key = trip is null ? null : trip.OnJob
            ? $"job|{trip.Signals?.Deadline}|{trip.Signals?.JobKey}|{trip.DestinationCity}" : "free";
        if (trip is null && gameRunning) return;
        if (key == activityKey) return;
        activityKey = key;
        activityStarted = key is null ? null : DateTime.UtcNow;
    }

    private void ToggleRecording(IWin32Window owner)
    {
        if (recording is not null)
        {
            manualStopJob = recordedJob ?? JobId(liveSnapshot);
            var result = FinishRecording();
            MessageBox.Show(owner, result switch
            {
                RecordingFinish.Saved => L("Grabación guardada.", "Recording saved."),
                RecordingFinish.Empty => L("No se grabaron muestras del juego.", "No game samples were recorded."),
                RecordingFinish.Failed => L("No se pudo guardar la grabación.", "The recording could not be saved."),
                _ => L("El archivo quedó disponible para intentar recuperarlo en Repeticiones.",
                    "The file is available for a recovery attempt in Replays.")
            }, Text);
            return;
        }
        if (demo || replayOpen || demoScenario is not null)
        {
            MessageBox.Show(owner, L("Sal de la demostración o reproducción antes de grabar.", "Exit demo or replay before recording."), Text);
            return;
        }
        if (StartRecording(automatic: false, Environment.TickCount64, JobId(liveSnapshot))) RefreshPresence();
        else MessageBox.Show(owner, settingsError ?? L("No se pudo grabar.", "Could not record."), Text);
    }

    private static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("eurotrucks2");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private void CopyDiagnostics()
    {
        var gameVersion = SteamLocator.IsValidGamePath(gamePath)
            ? FileVersionInfo.GetVersionInfo(Path.Combine(gamePath!, "bin", "win_x64", "eurotrucks2.exe")).FileVersion : null;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine,
                $"ETS2 Discord Presence {DesktopServices.Version.ToString(3)}",
                $"Versión de ETS2: {gameVersion ?? "No detectada"}",
                $"Revisión del lector: {lastRevision?.ToString() ?? "No detectada"}",
                gameStatus.Text, discordStatus.Text, setupError ?? ""));
            MessageBox.Show(this, L("Diagnóstico copiado al portapapeles.", "Diagnostics copied to the clipboard."), Text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, L("El portapapeles está ocupado. Intenta otra vez.", "The clipboard is busy. Try again."), Text);
        }
    }

    private void OpenExternal(string uri)
    {
        try { DesktopServices.Open(uri); }
        catch (Win32Exception) { MessageBox.Show(this, L("Windows no pudo abrir el enlace.", "Windows could not open the link."), Text); }
    }

    internal void RestoreWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ShowLicenses()
    {
        RestoreWindow();
        using var window = new Form
        {
            Text = "Licencias", Size = new Size(680, 500),
            StartPosition = FormStartPosition.CenterParent
        };
        using var telemetry = new StreamReader(AppResources.Open("RENCLOUD-LICENSE.txt"));
        using var sdk = new StreamReader(AppResources.Open("SCS-SDK-LICENSE.txt"));
        using var cityData = new StreamReader(AppResources.Open("CITY-DATA-LICENSE.txt"));
        using var fluent = new StreamReader(AppResources.Open("FLUENT-LICENSE.txt"));
        window.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = "RenCloud SCS SDK Plugin\r\n\r\n" + telemetry.ReadToEnd()
                + "\r\n\r\nSCS SDK\r\n\r\n" + sdk.ReadToEnd()
                + "\r\n\r\nETS2 City Coordinate Retriever\r\n\r\n" + cityData.ReadToEnd()
                + "\r\n\r\nMicrosoft Fluent UI System Icons\r\n\r\n" + fluent.ReadToEnd()
        });
        window.ShowDialog(this);
    }
}
