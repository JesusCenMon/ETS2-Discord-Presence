using System.ComponentModel;
using System.Diagnostics;
using DiscordRPC;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm : Form
{
    private static readonly TelemetrySnapshot Sample = new(false, true, "Maquinaria industrial para obras de infraestructura",
        "Lyon", "Turín", 0, 0, 215_000, 12, 1, 9 * 60 + 30, 10_800, 390);

    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly DiscordRpcClient discord = new(DiscordIdentity.ApplicationId);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CityLookup cities;
    private readonly Icon appIcon;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ContextMenuStrip settingsMenu = new();
    private readonly bool startMinimized;
    private readonly ToolStripMenuItem trayPause = new();
    private AppSettings preferences = AppSettings.Load();
    private PresenceText? lastPresence;
    private volatile bool discordReady;
    private volatile bool discordWasReady;
    private int connectionGeneration;
    private int publishedGeneration;
    private bool demo;
    private bool restartNeeded;
    private bool gamePrepared;
    private bool canLocateGame;
    private string? setupError;
    private string? settingsError;
    private string? gamePath;
    private string? updateUrl;
    private long nextGameCheck;
    private ulong? lastFrame;
    private long lastFrameAt;
    private string? city;
    private string? cityCandidate;
    private int citySamples;
    private uint? lastRevision;

    public MainForm(bool startMinimized = false)
    {
        this.startMinimized = startMinimized;
        Text = "ETS2 Discord Presence";
        ClientSize = new Size(1070, 710);
        MinimumSize = new Size(400, 390);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Foreground;
        using var iconData = AppResources.Open("ets2-presence.ico");
        appIcon = new Icon(iconData);
        Icon = appIcon;
        using var cityData = AppResources.Open("cities_default.json");
        cities = new CityLookup(cityData);
        BuildLayout();

        tray = new NotifyIcon { Icon = appIcon, Text = Text, Visible = true, ContextMenuStrip = trayMenu };
        trayMenu.Items.Add("Abrir panel", null, (_, _) => RestoreWindow());
        trayPause.Click += (_, _) => ToggleSharing();
        trayMenu.Items.Add(trayPause);
        trayMenu.Items.Add("Licencias", null, (_, _) => ShowLicenses());
        trayMenu.Items.Add("Salir", null, (_, _) => Close());
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
        settingsButton.Click += (_, _) => settingsMenu.Show(settingsButton, new Point(0, settingsButton.Height));
        updateButton.Click += (_, _) => { if (updateUrl is not null) OpenExternal(updateUrl); };
        BuildSettings();
        Shown += (_, _) => Start();
        FormClosed += (_, _) =>
        {
            lifetime.Cancel();
            timer.Dispose();
            discord.Dispose();
            tray.Dispose();
            trayMenu.Dispose();
            settingsMenu.Dispose();
            appIcon.Dispose();
            lifetime.Dispose();
        };
    }

    private async void Start()
    {
        if (startMinimized) Hide();
        discord.OnReady += (_, _) =>
        {
            discordWasReady = true;
            Interlocked.Increment(ref connectionGeneration);
            discordReady = true;
        };
        discord.OnClose += (_, _) => discordReady = false;
        discord.OnConnectionFailed += (_, _) => discordReady = false;
        discord.Initialize();
        DiscoverGame();
        RefreshPresence();
        timer.Start();

        try
        {
            var release = await DesktopServices.CheckForUpdateAsync(lifetime.Token);
            if (release is { } found && !IsDisposed)
            {
                updateUrl = found.Url;
                updateButton.Text = $"↓  Descargar v{found.Version}";
                updateButton.Visible = true;
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
            SetSetupError("No se pudo leer la biblioteca de Steam. Usa Buscar ETS2.", allowBrowse: true);
        }
    }

    private void PrepareGame(string? path)
    {
        gamePath = path;
        setupError = null;
        canLocateGame = false;
        if (path is null)
        {
            SetSetupError("ETS2 no aparece en Steam. Selecciona su carpeta.", allowBrowse: true);
            return;
        }
        try
        {
            restartNeeded = PluginInstaller.Prepare(path) && IsGameRunning();
            gamePrepared = true;
        }
        catch (UnauthorizedAccessException)
        {
            SetSetupError("Windows protegió la carpeta de ETS2. Abre esta app como administrador para prepararla.", allowBrowse: false);
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
        using var picker = new FolderBrowserDialog { Description = "Selecciona la carpeta de Euro Truck Simulator 2" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        PrepareGame(picker.SelectedPath);
        if (gamePrepared) SavePreferences(preferences with { GamePath = picker.SelectedPath });
        RefreshPresence();
    }

    private void BuildSettings()
    {
        AddSetting("Compartir carga", preferences.ShowCargo, value => SavePreferences(preferences with { ShowCargo = value }));
        AddSetting("Compartir ruta", preferences.ShowRoute, value => SavePreferences(preferences with { ShowRoute = value }));
        AddSetting("Compartir ciudad cercana", preferences.ShowCity, value => SavePreferences(preferences with { ShowCity = value }));
        AddSetting("Compartir distancia", preferences.ShowDistance, value => SavePreferences(preferences with { ShowDistance = value }));
        AddSetting("Dar prioridad a la ruta en Discord", preferences.RouteFirst, value => SavePreferences(preferences with { RouteFirst = value }));
        settingsMenu.Items.Add(new ToolStripSeparator());
        AddSetting("Vista compacta", preferences.Compact, value =>
        {
            SavePreferences(preferences with { Compact = value });
            ApplyCompactSize();
        });
        AddSetting("Demostración local", false, value =>
        {
            demo = value;
            RefreshPresence();
        });
        AddSetting("Iniciar minimizada con Windows", DesktopServices.StartsWithWindows(), value =>
        {
            try { DesktopServices.SetStartup(value); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                MessageBox.Show(this, "Windows no permitió cambiar el inicio automático.", Text);
            }
        });
        settingsMenu.Items.Add("Buscar ETS2…", null, (_, _) => LocateGame());
        settingsMenu.Items.Add("Reintentar preparación", null, (_, _) =>
        {
            PrepareGame(SteamLocator.IsValidGamePath(gamePath) ? gamePath : null);
            if (gamePath is null) DiscoverGame();
            RefreshPresence();
        });
        settingsMenu.Items.Add("Copiar diagnóstico", null, (_, _) => CopyDiagnostics());
        ApplyCompactSize();
    }

    private void AddSetting(string text, bool enabled, Action<bool> change)
    {
        var item = new ToolStripMenuItem(text) { Checked = enabled, CheckOnClick = true };
        item.CheckedChanged += (_, _) => change(item.Checked);
        settingsMenu.Items.Add(item);
    }

    private void SavePreferences(AppSettings updated)
    {
        preferences = updated;
        try
        {
            preferences.Save();
            settingsError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            settingsError = "Windows no permitió guardar tus preferencias.";
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
        var action = preferences.Sharing ? "Pausar publicación" : "Reanudar publicación";
        toggle.Text = $"{(preferences.Sharing ? "Ⅱ" : "▶")}  {action}";
        trayPause.Text = action;
    }

    private void RefreshPresence()
    {
        var ready = discordReady;
        SetStatus(discordStatus, ready ? "Discord · Conectado" :
            discordWasReady ? "Discord · Reconectando…" : "Discord · Esperando la app de escritorio", ready);

        var now = Environment.TickCount64;
        var running = IsGameRunning();
        if (!running) restartNeeded = false;
        if (now >= nextGameCheck && (!gamePrepared || !SteamLocator.IsValidGamePath(gamePath))) DiscoverGame();

        TelemetrySnapshot? telemetry = null;
        string? readError = null;
        if (running) TelemetryReader.TryRead(out telemetry, out readError);
        var frozen = telemetry is not null && !IsFresh(telemetry, now);
        if (frozen) telemetry = null;
        if (telemetry is null) ResetLocation(running);
        else lastRevision = telemetry.PluginRevision;

        locate.Visible = canLocateGame && telemetry is null;
        launch.Visible = !running;
        var status = telemetry is not null ? "ETS2 · Viaje detectado" : frozen ? "ETS2 · Datos detenidos"
            : setupError is not null ? "ETS2 · Revisar preparación"
            : running ? "ETS2 · Esperando la partida" : "ETS2 · Cerrado";
        SetStatus(gameStatus, status, telemetry is not null);

        var nearby = telemetry is null ? null : TrackCity(cities.FindNearby(telemetry.X, telemetry.Z));
        var trip = demo ? Sample : telemetry;
        var displayedCity = demo ? "Chambéry" : nearby;
        var presence = trip is null ? null : PresenceFormatter.Format(trip, displayedCity, preferences);
        RenderTrip(trip, displayedCity, presence);
        guidance.Text = settingsError ?? (demo ? "Demostración local. El viaje de ejemplo no se publica."
            : !preferences.Sharing ? "La publicación está pausada. Puedes reanudarla desde este panel o la bandeja."
            : telemetry is not null ? (ready ? "Actividad preparada para Discord." : "Abre Discord de escritorio para compartir tu viaje.")
            : setupError ?? (restartNeeded ? "Reinicia ETS2 para activar la lectura del viaje."
                : frozen ? "El juego dejó de enviar datos. Se retiró la actividad anterior."
                : running ? readError ?? "Entra a una partida." : "Abre ETS2 para empezar."));
        Publish(!demo && preferences.Sharing && ready ? presence : null);
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

    private void ResetLocation(bool running)
    {
        city = null;
        cityCandidate = null;
        citySamples = 0;
        if (!running) lastFrame = null;
    }

    private void Publish(PresenceText? presence)
    {
        var generation = Volatile.Read(ref connectionGeneration);
        if (presence == lastPresence && (presence is null || generation == publishedGeneration)) return;
        if (presence is null)
            discord.ClearPresence();
        else
            discord.SetPresence(new RichPresence { Details = presence.Details, State = presence.State });
        lastPresence = presence;
        publishedGeneration = generation;
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
            MessageBox.Show(this, "Diagnóstico copiado al portapapeles.", Text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "El portapapeles está ocupado. Intenta otra vez.", Text);
        }
    }

    private void OpenExternal(string uri)
    {
        try { DesktopServices.Open(uri); }
        catch (Win32Exception) { MessageBox.Show(this, "Windows no pudo abrir el enlace.", Text); }
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
