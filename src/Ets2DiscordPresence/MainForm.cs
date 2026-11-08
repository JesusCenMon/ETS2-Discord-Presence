using System.Diagnostics;
using System.Drawing.Drawing2D;
using DiscordRPC;
using Button = System.Windows.Forms.Button;

namespace Ets2DiscordPresence;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(12, 21, 27);
    private static readonly Color Surface = Color.FromArgb(21, 35, 43);
    private static readonly Color Foreground = Color.FromArgb(244, 247, 244);
    private static readonly Color Muted = Color.FromArgb(168, 190, 195);
    private static readonly Color Mint = Color.FromArgb(128, 229, 201);
    private static readonly Color Amber = Color.FromArgb(245, 192, 117);

    private readonly Label gameStatus = TextLabel("Buscando ETS2…", 10);
    private readonly Label discordStatus = TextLabel("Buscando Discord…", 10);
    private readonly Label details = TextLabel("Sin viaje activo", 17, bold: true);
    private readonly Label state = TextLabel("Aquí aparecerá la actividad de tu viaje.", 11);
    private readonly Label guidance = TextLabel("Preparando la conexión…", 10);
    private readonly Button toggle = MakeButton("Pausar actividad");
    private readonly Button locate = MakeButton("Buscar ETS2");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly DiscordRpcClient discord = new(DiscordIdentity.ApplicationId);
    private readonly CityLookup cities;
    private readonly Icon appIcon;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip trayMenu = new();
    private PresenceText? lastPresence;
    private volatile bool discordReady;
    private bool sharing = true;
    private bool restartNeeded;
    private bool gamePrepared;
    private bool canLocateGame;
    private string? setupError;

    public MainForm()
    {
        Text = "ETS2 Discord Presence";
        ClientSize = new Size(1030, 600);
        MinimumSize = new Size(760, 530);
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
        trayMenu.Items.Add("Licencias", null, (_, _) => ShowLicenses());
        trayMenu.Items.Add("Salir", null, (_, _) => Close());
        tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        timer.Tick += (_, _) => RefreshPresence();
        toggle.Click += (_, _) => ToggleSharing();
        locate.Click += (_, _) => LocateGame();
        Shown += (_, _) => Start();
        FormClosed += (_, _) =>
        {
            timer.Dispose();
            discord.Dispose();
            tray.Dispose();
            trayMenu.Dispose();
            appIcon.Dispose();
        };
    }

    private void BuildLayout()
    {
        var shell = Grid(1, 2);
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var header = Grid(2, 1);
        header.Padding = new Padding(25, 0, 25, 0);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(TextLabel("ETS2 Discord Presence", 17, bold: true), 0, 0);
        var desktop = TextLabel("EURO TRUCK SIMULATOR 2", 9, bold: true);
        desktop.ForeColor = Mint;
        desktop.AutoSize = true;
        header.Controls.Add(desktop, 1, 0);
        shell.Controls.Add(header, 0, 0);

        var body = Grid(2, 1);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.Controls.Add(new RoadArtwork { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        shell.Controls.Add(body, 0, 1);

        var dashboard = Grid(1, 7);
        dashboard.BackColor = Background;
        dashboard.Padding = new Padding(24, 19, 24, 20);
        foreach (var height in new[] { 37, 32, 32 })
            dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 54, 46, 42 })
            dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        dashboard.Controls.Add(TextLabel("Estado de conexión", 14, bold: true), 0, 0);
        dashboard.Controls.Add(gameStatus, 0, 1);
        dashboard.Controls.Add(discordStatus, 0, 2);
        var preview = Grid(1, 3);
        preview.BackColor = Surface;
        preview.Margin = new Padding(0, 12, 0, 6);
        preview.Padding = new Padding(16, 10, 16, 10);
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = TextLabel("VISTA PREVIA EN DISCORD", 9, bold: true);
        caption.ForeColor = Mint;
        preview.Controls.Add(caption, 0, 0);
        preview.Controls.Add(details, 0, 1);
        state.TextAlign = ContentAlignment.TopLeft;
        preview.Controls.Add(state, 0, 2);
        dashboard.Controls.Add(preview, 0, 3);
        dashboard.Controls.Add(guidance, 0, 4);
        dashboard.Controls.Add(toggle, 0, 5);
        dashboard.Controls.Add(locate, 0, 6);
        locate.Visible = false;
        body.Controls.Add(dashboard, 1, 0);
    }

    private void Start()
    {
        discord.OnReady += (_, _) => discordReady = true;
        discord.OnClose += (_, _) => discordReady = false;
        discord.OnConnectionFailed += (_, _) => discordReady = false;
        discord.Initialize();

        try
        {
            var saved = AppSettings.Load().GamePath;
            PrepareGame(SteamLocator.IsValidGamePath(saved) ? saved : SteamLocator.FindGame());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetSetupError("No se pudo leer la biblioteca de Steam. Puedes buscar ETS2.", allowBrowse: true);
        }

        RefreshPresence();
        timer.Start();
    }

    private void PrepareGame(string? path)
    {
        setupError = null;
        canLocateGame = false;
        if (path is null)
        {
            SetSetupError("Selecciona la carpeta de ETS2 si está en otra biblioteca.", allowBrowse: true);
            return;
        }
        try
        {
            var installed = PluginInstaller.Prepare(path);
            restartNeeded = installed && IsGameRunning();
            gamePrepared = true;
        }
        catch (UnauthorizedAccessException)
        {
            SetSetupError("Windows protegió la carpeta de ETS2. Abre esta app como administrador para prepararla.", allowBrowse: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
        {
            SetSetupError(ex.Message, allowBrowse: true);
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
        if (gamePrepared)
        {
            try { new AppSettings(picker.SelectedPath).Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        RefreshPresence();
    }

    private void ToggleSharing()
    {
        sharing = !sharing;
        toggle.Text = sharing ? "Pausar actividad" : "Reanudar actividad";
        RefreshPresence();
    }

    private void RefreshPresence()
    {
        var ready = discordReady;
        SetStatus(discordStatus, ready ? "Discord  ·  Conectado" : "Discord  ·  Esperando la app de escritorio", ready);
        var hasTravel = TelemetryReader.TryRead(out var telemetry, out var readError) && telemetry is not null;
        locate.Visible = canLocateGame && !hasTravel;

        if (hasTravel && telemetry is not null)
        {
            SetStatus(gameStatus, "ETS2  ·  Viaje detectado", true);
            var presence = PresenceFormatter.Format(telemetry, cities.FindNearby(telemetry.X, telemetry.Z));
            details.Text = sharing ? presence.Details : "Actividad en pausa";
            state.Text = sharing ? presence.State : "Reanuda para compartir los datos del viaje.";
            guidance.Text = !sharing ? "La actividad está oculta." : ready ? "Puedes minimizar la ventana mientras juegas." : "Abre Discord de escritorio para compartir tu viaje.";
            Publish(sharing && ready ? presence : null);
            return;
        }

        var gameRunning = IsGameRunning();
        if (!gameRunning) restartNeeded = false;
        SetStatus(gameStatus, gamePrepared ? "ETS2  ·  Esperando el viaje" : "ETS2  ·  Preparación pendiente", false);
        details.Text = sharing ? "Sin viaje activo" : "Actividad en pausa";
        state.Text = "Aquí aparecerán la entrega y el destino.";
        guidance.Text = setupError ?? (restartNeeded
            ? "Reinicia ETS2 una vez para que empiece a enviar datos."
            : gameRunning ? readError ?? "Entra a una partida para compartir tu viaje." : "Abre ETS2 para empezar.");
        Publish(null);
    }

    private void Publish(PresenceText? presence)
    {
        if (presence == lastPresence) return;
        if (presence is null)
            discord.ClearPresence();
        else
            discord.SetPresence(new RichPresence { Details = presence.Details, State = presence.State });
        lastPresence = presence;
    }

    private static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("eurotrucks2");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private void RestoreWindow()
    {
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

    private static void SetStatus(Label label, string text, bool active)
    {
        label.Text = text;
        label.ForeColor = active ? Mint : Amber;
    }

    private static TableLayoutPanel Grid(int columns, int rows) => new()
    {
        Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty
    };

    private static Label TextLabel(string text, int size, bool bold = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, ForeColor = bold ? Foreground : Muted,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    private static Button MakeButton(string text)
    {
        var button = new Button
        {
            Text = text, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4),
            FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Foreground,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(64, 97, 103);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 61, 68);
        return button;
    }

    private sealed class RoadArtwork : Panel
    {
        private readonly Bitmap image;

        public RoadArtwork()
        {
            using var stream = AppResources.Open("hero-truck.png");
            using var source = Image.FromStream(stream);
            image = new Bitmap(source);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width == 0 || Height == 0) return;
            var scale = Math.Max((float)Width / image.Width, (float)Height / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(image, (Width - width) / 2, (Height - height) / 2, width, height);
            var shadeArea = new Rectangle(0, Height * 2 / 3, Width, Height - Height * 2 / 3);
            using var shade = new LinearGradientBrush(shadeArea, Color.Transparent, Color.FromArgb(224, Background), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(shade, shadeArea);
            using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "Tu viaje en Discord", titleFont,
                new Rectangle(24, Height - 75, Math.Max(1, Width - 48), 52), Foreground,
                TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image.Dispose();
            base.Dispose(disposing);
        }
    }
}
