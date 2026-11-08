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
    private readonly PresencePublication publication = new();
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
        this.startMinimized = startMinimized;
        journal = new TripJournal(LocalData.JournalPath(preferences.DriverProfile));
        Text = "ETS2 Discord Presence";
        ClientSize = new Size(1070, 710);
        MinimumSize = new Size(400, 390);
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
        DpiChanged += (_, _) => ArrangeLayout();
        timer.Tick += (_, _) => RefreshPresence();
        toggle.Click += (_, _) => ToggleSharing();
        locate.Click += (_, _) => LocateGame();
        launch.Click += (_, _) => OpenExternal("steam://rungameid/227300");
        settingsButton.Click += (_, _) => ShowSettings();
        ApplyCompactSize();
        ApplyLanguage();
        ConfigurePanels();
        Restyle(this);
        ScaleText(this);
        Shown += (_, _) => Start();
        FormClosed += (_, _) =>
        {
            lifetime.Cancel();
            timer.Dispose();
            discord.Dispose();
            tray.Dispose();
            trayMenu.Dispose();
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
        AppUpdater.CleanWorker(Application.ExecutablePath);
        if (AppUpdater.ConsumeFailure(Application.ExecutablePath))
            settingsError = L("La actualización no se pudo completar.", "The update could not be completed.");
        discord.OnReady += (_, _) =>
        {
            discordWasReady = true;
            Interlocked.Increment(ref connectionGeneration);
            discordReady = true;
        };
        discord.OnPresenceUpdate += (_, message) =>
        {
            var sent = lastSent;
            discordConfirmed = sent is not null && message.Presence is { } ack
                && ack.Details == sent.Details && ack.State == sent.State;
        };
        discord.OnClose += (_, _) => { discordReady = false; discordConfirmed = false; };
        discord.OnConnectionFailed += (_, _) => discordReady = false;
        discord.Initialize();
        DiscoverGame();
        RefreshPresence();
        timer.Start();
        if (!retentionChecked)
        {
            retentionChecked = true;
            try { LocalData.PruneRecordings(preferences.RetentionDays, recordingPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        try
        {
            var release = await DesktopServices.CheckForUpdateAsync(lifetime.Token);
            if (release is { } found && !IsDisposed)
            {
                availableUpdate = found;
            }
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
                journal.Flush();
                journal = new TripJournal(LocalData.JournalPath(updated.DriverProfile));
                completedTrip = null;
            }
            preferences = updated;
            settingsError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            settingsError = L("Windows no permitió guardar tus preferencias.", "Windows did not allow saving your settings.");
        }
        RefreshPresence();
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
        if (telemetry is null) ResetLocation(running);
        else lastRevision = telemetry.PluginRevision;

        liveSnapshot = telemetry;
        locate.Visible = canLocateGame && telemetry is null;
        launch.Visible = !running;
        var status = telemetry is not null ? L("ETS2 · Viaje detectado", "ETS2 · Trip detected") : frozen ? L("ETS2 · Datos detenidos", "ETS2 · Data stopped")
            : setupError is not null ? L("ETS2 · Revisar preparación", "ETS2 · Check setup")
            : running ? L("ETS2 · Esperando la partida", "ETS2 · Waiting for game session") : L("ETS2 · Cerrado", "ETS2 · Closed");
        SetStatus(gameStatus, status, telemetry is not null);

        var nearby = telemetry is null ? null : TrackCity(cities.FindNearby(telemetry.X, telemetry.Z));
        if (telemetry is not null) TrackCountry(cities.FindCountry(telemetry.X, telemetry.Z));
        var ending = journal.Observe(telemetry, DateTimeOffset.UtcNow, now);
        UpdateRecording(telemetry, ending, now);
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
            _ => demo ? Sample : telemetry
        };
        var isExample = demo || demoScenario is not null;
        var displayedCity = demoScenario == "roam" ? "Madrid" : isExample ? "Chambéry" : nearby;
        var presenceSettings = privateDestination ? preferences with { ShowRoute = false, ShowCity = false } : preferences;
        if (trip?.OnJob == false && preferences.Features.HideRoamingLocation)
            presenceSettings = presenceSettings with { ShowCity = false };
        if (trip?.OnJob == true && preferences.Features.HideDeliveryLocation)
            presenceSettings = presenceSettings with { ShowCity = false, ShowRoute = false };
        var country = demoScenario == "roam" ? "ES" : isExample ? "FR" : currentCountry;
        var presence = trip is null ? null : PresenceFormatter.Format(trip, displayedCity, presenceSettings, country);
        UpdateActivity(telemetry);
        if (presence is not null)
            presence = presence with
            {
                Started = preferences.ActivityTimer ? isExample ? DateTime.UtcNow.AddMinutes(-3) : preferences.Features.SessionTimer ? sessionStarted : activityStarted : null,
                Image = preferences.ActivityImage ? DiscordIdentity.ImageFor(presenceSettings.ShowCity ? country : null) : null,
                Badge = preferences.ActivityImage && trip is not null
                    ? DiscordIdentity.ActivityBadge(trip.Paused ? "paused" : trip.OnJob ? "delivery" : "roam") : null
            };
        RenderTrip(trip, displayedCity, presence);
        RenderExtras(trip, now, running, ready);
        RenderTools(trip, telemetry, ready);
        UpdateLiveNotices(telemetry, discordReady);
        RenderDashboard(trip, running, ready, now);
        tripMap.UpdateMap(trip, cities, English, isLight, isExample, !preferences.Features.LowPower || Visible);
        guidance.Text = settingsError ?? (journal.Error is not null ? L("No se pudo acceder al historial local.", "The local trip history is unavailable.") : null) ?? (replayOpen ? L("Reproducción local. No se publica en Discord.", "Local replay. Discord publishing is paused.")
            : demo ? L("Demostración local. El viaje de ejemplo no se publica.", "Local demo. The sample trip stays local.")
            : !preferences.Sharing ? L("La publicación está pausada. Puedes reanudarla desde este panel o la bandeja.", "Publishing is paused. Resume here or from the tray.")
            : telemetry is not null ? (ready ? L("Actividad preparada para Discord.", "Activity ready for Discord.") : L("Abre Discord de escritorio para compartir tu viaje.", "Open Discord desktop to share your trip."))
            : preferences.ReaderDisabled ? L("Lector desactivado. Puedes restaurarlo en Ajustes.", "Reader disabled. Restore it in Settings.")
            : setupError ?? (restartNeeded ? L("Reinicia ETS2 para activar la lectura del viaje.", "Restart ETS2 to enable trip data.")
                : frozen ? L("El juego dejó de enviar datos. Se retiró la actividad anterior.", "The game stopped sending data. The previous activity was cleared.")
                : running ? (English ? L("Entra a una partida.", "Enter a game session or check the reader in Settings.") : readError ?? "Entra a una partida.") : L("Abre ETS2 para empezar.", "Launch ETS2 to begin.")));
        var sharing = preferences.Sharing && !privateForSession && !(privateUntil > DateTimeOffset.UtcNow);
        if (!sharing && preferences.Sharing)
            guidance.Text = L("La actividad está oculta temporalmente.", "Activity is temporarily hidden.");
        else if (demoScenario is not null)
            guidance.Text = L("Prueba local. No se publica en Discord.", "Local test. Nothing is published to Discord.");
        else if (routeChangedUntil > now && trip?.OnJob == true)
            guidance.Text = L("La distancia o la llegada cambiaron. ETS2 pudo recalcular la ruta.",
                "Distance or arrival time changed. ETS2 may have recalculated the route.");
        else if (quotaWarning is not null && recording is not null) guidance.Text = quotaWarning;
        var outgoing = PresencePublication.Select(presence, simulated, sharing, discordReady);
        RenderPreview(outgoing, presence);
        Publish(outgoing);
    }

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

    private void TrackCountry(string? country)
    {
        if (country is null) currentCountry = null;
        if (country != countryCandidate)
        {
            countryCandidate = country;
            countrySamples = 1;
        }
        else if (countrySamples < 3) countrySamples++;
        if (countrySamples == 3) currentCountry = countryCandidate;
    }

    private void ResetLocation(bool running)
    {
        city = null;
        cityCandidate = null;
        citySamples = countrySamples = 0;
        currentCountry = countryCandidate = null;
        if (!running) lastFrame = null;
    }

    private void Publish(PresenceText? presence)
    {
        if (!publication.ShouldSend(presence, Volatile.Read(ref connectionGeneration))) return;
        lastSent = presence;
        discordConfirmed = false;
        if (presence is null) discord.ClearPresence();
        else discord.SetPresence(new RichPresence
        {
            Details = presence.Details, State = presence.State,
            Assets = presence.Image is { } image ? new Assets
            {
                LargeImageKey = image, LargeImageText = DiscordIdentity.ActivityName, SmallImageKey = presence.Badge
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
            FinishRecording();
            MessageBox.Show(owner, L("Grabación guardada.", "Recording saved."), Text);
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
        using var cityData = new StreamReader(AppResources.Open("CITY-DATA-LICENSE.txt"));
        window.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = "RenCloud SCS SDK Plugin\r\n\r\n" + telemetry.ReadToEnd()
                + "\r\n\r\nETS2 City Coordinate Retriever\r\n\r\n" + cityData.ReadToEnd()
        });
        window.ShowDialog(this);
    }
}
