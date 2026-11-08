using DiscordRPC;
using System.Drawing;
using System.Drawing.Drawing2D;
using Button = System.Windows.Forms.Button;

namespace Ets2DiscordPresence;

internal sealed class MainForm : Form
{
    private static readonly Color Back = Color.FromArgb(14, 19, 23);
    private static readonly Color Side = Color.FromArgb(23, 29, 34);
    private static readonly Color Card = Color.FromArgb(32, 40, 46);
    private static readonly Color Muted = Color.FromArgb(184, 197, 201);
    private static readonly Color Accent = Color.FromArgb(29, 157, 139);
    private static readonly Color Fore = Color.FromArgb(244, 247, 246);

    private readonly TextBox gamePathBox = new();
    private readonly Button browseButton = new() { Text = "Buscar" };
    private readonly Button installButton = new() { Text = "Instalar componente", Height = 46 };
    private readonly Button startButton = new() { Text = "INICIAR PRESENCIA  →", Height = 52 };
    private readonly Label statusLabel = new() { Height = 75 };
    private readonly Label detailsLabel = new() { Height = 56 };
    private readonly Label stateLabel = new() { Height = 64 };
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly CityLookup cities;
    private DiscordRpcClient? discord;
    private string? lastPresence;
    private bool running;

    public MainForm()
    {
        Text = "ETS2 Discord Presence";
        Size = new Size(1360, 820);
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Back;
        ForeColor = Fore;
        var settings = AppSettings.Load();
        gamePathBox.Text = !string.IsNullOrWhiteSpace(settings.GamePath) ? settings.GamePath : SteamLocator.FindGame() ?? "";
        cities = new CityLookup(Path.Combine(AppContext.BaseDirectory, "cities_default.json"));
        BuildLayout();

        trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = Text, Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        menu.Items.Add("Salir", null, (_, _) => Close());
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
        browseButton.Click += (_, _) => BrowseGame();
        installButton.Click += async (_, _) => await InstallAsync();
        startButton.Click += (_, _) => { if (running) Stop(); else Start(); };
        timer.Tick += (_, _) => UpdatePresence();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosed += (_, _) => { Stop(); trayIcon.Visible = false; trayIcon.Dispose(); };
        UpdateSetupStatus();
        if (PluginInstaller.IsInstalled(gamePathBox.Text) && IsValidAppId(DiscordIdentity.ApplicationId)) Start();
    }

    private void BuildLayout()
    {
        var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        Controls.Add(frame);

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Side, Padding = new Padding(25, 28, 22, 20) };
        frame.Controls.Add(left, 0, 0);
        var l = Flow(left);
        l.Controls.Add(Label("ETS2", 28, 45, Fore, true));
        l.Controls.Add(Label("DISCORD PRESENCE", 10, 46, Accent, true));
        l.Controls.Add(Label("Tu viaje en Discord", 19, 54, Fore, true));
        l.Controls.Add(Label("Lo que haces en la carretera se convierte en tu actividad de Discord.", 10, 75, Muted));
        l.Controls.Add(Label("VISTA PREVIA", 9, 52, Muted, true));
        var preview = new Panel { BackColor = Card, Size = new Size(246, 190), Padding = new Padding(15) };
        var p = Flow(preview);
        p.Controls.Add(Label("●  ETS2 Discord Presence", 10, 35, Accent, true));
        detailsLabel.Text = "Esperando una partida...";
        detailsLabel.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        detailsLabel.ForeColor = Fore;
        stateLabel.Text = "Abre ETS2 para ver tu actividad.";
        stateLabel.ForeColor = Muted;
        p.Controls.Add(detailsLabel);
        p.Controls.Add(stateLabel);
        l.Controls.Add(preview);
        l.Controls.Add(Label("ESTADO", 9, 54, Muted, true));
        statusLabel.ForeColor = Muted;
        l.Controls.Add(statusLabel);

        frame.Controls.Add(new HeroPanel(Path.Combine(AppContext.BaseDirectory, "hero-truck.png")) { Dock = DockStyle.Fill }, 1, 0);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Side, Padding = new Padding(24, 29, 20, 20) };
        frame.Controls.Add(right, 2, 0);
        var r = Flow(right);
        r.Controls.Add(Label("Ponerlo en marcha", 19, 57, Fore, true));
        r.Controls.Add(Label("Instala el componente una vez. Después se conecta solo al abrirlo.", 10, 80, Muted));
        r.Controls.Add(Label("1  EURO TRUCK SIMULATOR 2", 9, 41, Muted, true));
        var gameRow = new TableLayoutPanel { Size = new Size(245, 42), ColumnCount = 2, RowCount = 1 };
        gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 69));
        StyleInput(gamePathBox);
        gamePathBox.Dock = DockStyle.Fill;
        StyleSecondary(browseButton);
        browseButton.Dock = DockStyle.Fill;
        gameRow.Controls.Add(gamePathBox, 0, 0);
        gameRow.Controls.Add(browseButton, 1, 0);
        r.Controls.Add(gameRow);
        StyleSecondary(installButton);
        installButton.Width = 245;
        installButton.Margin = new Padding(0, 12, 0, 0);
        r.Controls.Add(installButton);
        r.Controls.Add(Label("2  DISCORD", 9, 55, Muted, true));
        r.Controls.Add(Label("Mantén abierta la aplicación de Discord en tu PC. La conexión es automática.", 10, 74, Fore));
        r.Controls.Add(Label("3  EMPEZAR", 9, 45, Muted, true));
        StylePrimary(startButton);
        r.Controls.Add(startButton);
        r.Controls.Add(Label("Puedes minimizar la ventana mientras conduces.", 10, 75, Muted));
    }

    private static FlowLayoutPanel Flow(Control parent)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = Padding.Empty };
        parent.Controls.Add(flow);
        void FitChildren()
        {
            var width = Math.Max(120, flow.ClientSize.Width - 22);
            foreach (Control child in flow.Controls)
                if (child.Width != width) child.Width = width;
        }
        flow.SizeChanged += (_, _) => FitChildren();
        flow.ControlAdded += (_, _) => FitChildren();
        return flow;
    }

    private static Label Label(string text, int size, int height, Color color, bool bold = false) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color, Size = new Size(248, height), Margin = Padding.Empty
    };

    private static void StyleInput(TextBox box)
    {
        box.BackColor = Card;
        box.ForeColor = Fore;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10);
        box.Height = 38;
    }

    private static void StyleSecondary(Button button)
    {
        button.BackColor = Color.FromArgb(39, 59, 61);
        button.ForeColor = Fore;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.Margin = Padding.Empty;
    }

    private static void StylePrimary(Button button)
    {
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        button.Width = 245;
        button.Margin = Padding.Empty;
    }

    private void BrowseGame()
    {
        using var picker = new FolderBrowserDialog { Description = "Selecciona la carpeta Euro Truck Simulator 2" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        gamePathBox.Text = picker.SelectedPath;
        UpdateSetupStatus();
    }

    private async Task InstallAsync()
    {
        installButton.Enabled = false;
        statusLabel.Text = "Descargando e instalando...";
        try
        {
            statusLabel.Text = await PluginInstaller.InstallAsync(gamePathBox.Text.Trim());
            SaveSettings();
            if (IsValidAppId(DiscordIdentity.ApplicationId)) Start();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or TaskCanceledException)
        {
            statusLabel.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "No se pudo instalar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { installButton.Enabled = true; }
    }

    private void Start()
    {
        if (!SteamLocator.IsValidGamePath(gamePathBox.Text.Trim()))
        {
            MessageBox.Show(this, "Primero selecciona la carpeta de ETS2.", Text); return;
        }
        if (!PluginInstaller.IsInstalled(gamePathBox.Text.Trim()))
        {
            MessageBox.Show(this, "Primero pulsa 'Instalar componente'.", Text); return;
        }
        if (!IsValidAppId(DiscordIdentity.ApplicationId))
        {
            MessageBox.Show(this, "La conexión de Discord aún no está preparada en esta versión de prueba.", Text); return;
        }
        try
        {
            SaveSettings();
            discord = new DiscordRpcClient(DiscordIdentity.ApplicationId);
            discord.Initialize();
            running = true;
            startButton.Text = "DETENER PRESENCIA";
            timer.Start();
            UpdatePresence();
        }
        catch (Exception ex)
        {
            discord?.Dispose();
            discord = null;
            statusLabel.Text = $"Discord no está listo: {ex.Message}";
        }
    }

    private void Stop()
    {
        timer.Stop();
        running = false;
        lastPresence = null;
        discord?.ClearPresence();
        discord?.Dispose();
        discord = null;
        startButton.Text = "INICIAR PRESENCIA  →";
        if (!IsDisposed) statusLabel.Text = "Detenido";
    }

    private void UpdatePresence()
    {
        if (discord is null) return;
        if (!TelemetryReader.TryRead(out var telemetry, out var error) || telemetry is null)
        {
            if (lastPresence is not null) { discord.ClearPresence(); lastPresence = null; }
            statusLabel.Text = error ?? "Esperando ETS2...";
            detailsLabel.Text = "Esperando una partida...";
            stateLabel.Text = "Abre ETS2 para ver tu actividad.";
            return;
        }
        var city = cities.FindNearby(telemetry.X, telemetry.Z);
        var presence = PresenceFormatter.Format(telemetry, city);
        var key = presence.Details + "|" + presence.State;
        if (key != lastPresence)
        {
            discord.SetPresence(new RichPresence { Details = presence.Details, State = presence.State });
            lastPresence = key;
        }
        statusLabel.Text = "Leyendo ETS2 y actualizando Discord";
        detailsLabel.Text = presence.Details;
        stateLabel.Text = presence.State;
    }

    private void UpdateSetupStatus() => statusLabel.Text = SteamLocator.IsValidGamePath(gamePathBox.Text)
        ? "ETS2 encontrado. Instala el componente y abre Discord."
        : "Selecciona la carpeta donde está instalado ETS2.";

    private void SaveSettings() => new AppSettings(gamePathBox.Text.Trim()).Save();
    private static bool IsValidAppId(string value) => value.Length is >= 17 and <= 20 && value.All(char.IsAsciiDigit);
    private void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private sealed class HeroPanel : Panel
    {
        private readonly Image? image;
        public HeroPanel(string path)
        {
            BackColor = Back;
            DoubleBuffered = true;
            if (File.Exists(path)) image = Image.FromFile(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image is not null)
            {
                var scale = Math.Max((float)Width / image.Width, (float)Height / image.Height);
                var width = image.Width * scale;
                var height = image.Height * scale;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(image, (Width - width) / 2, (Height - height) / 2, width, height);
            }
            using var shade = new LinearGradientBrush(new Rectangle(0, Height - 195, Width, 195), Color.FromArgb(0, Back), Color.FromArgb(235, Back), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(shade, 0, Height - 195, Width, 195);
            using var titleFont = new Font("Segoe UI", 20, FontStyle.Bold);
            using var subtitleFont = new Font("Segoe UI", 10);
            using var white = new SolidBrush(Fore);
            using var soft = new SolidBrush(Color.FromArgb(204, 216, 217));
            e.Graphics.DrawString("Cada ruta cuenta.", titleFont, white, 28, Height - 132);
            e.Graphics.DrawString("Comparte tu viaje mientras conduces.", subtitleFont, soft, 30, Height - 86);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
