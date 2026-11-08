using DiscordRPC;
using System.Drawing;
using Button = System.Windows.Forms.Button;

namespace Ets2DiscordPresence;

internal sealed class MainForm : Form
{
    private readonly TextBox gamePathBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox appIdBox = new() { Dock = DockStyle.Fill, PlaceholderText = "ID de la aplicación de Discord" };
    private readonly Button browseButton = new() { Text = "Buscar...", AutoSize = true };
    private readonly Button installButton = new() { Text = "Instalar componente de ETS2", AutoSize = true };
    private readonly Button startButton = new() { Text = "Iniciar", AutoSize = true };
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Preparando..." };
    private readonly Label previewLabel = new() { AutoSize = true, Text = "La vista previa aparecerá al abrir ETS2." };
    private readonly LinkLabel helpLink = new() { AutoSize = true, Text = "¿Dónde encuentro el ID de Discord?" };
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly CityLookup cities;
    private DiscordRpcClient? discord;
    private string? lastPresence;
    private bool running;

    public MainForm()
    {
        Text = "ETS2 Discord Presence";
        Width = 590;
        Height = 330;
        MinimumSize = new Size(520, 310);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var settings = AppSettings.Load();
        gamePathBox.Text = !string.IsNullOrWhiteSpace(settings.GamePath) ? settings.GamePath : SteamLocator.FindGame() ?? "";
        appIdBox.Text = Environment.GetEnvironmentVariable("DISCORD_APP_ID") ?? settings.DiscordAppId;
        cities = new CityLookup(Path.Combine(AppContext.BaseDirectory, "cities_default.json"));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Carpeta de Euro Truck Simulator 2", AutoSize = true }, 0, 0);
        layout.Controls.Add(gamePathBox, 0, 1);
        layout.Controls.Add(browseButton, 1, 1);
        layout.Controls.Add(installButton, 0, 2);
        layout.Controls.Add(new Label { Text = "Discord", AutoSize = true, Margin = new Padding(0, 16, 0, 0) }, 0, 3);
        layout.Controls.Add(appIdBox, 0, 4);
        layout.Controls.Add(helpLink, 0, 5);
        layout.Controls.Add(startButton, 0, 6);
        var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        info.Controls.Add(statusLabel);
        info.Controls.Add(previewLabel);
        layout.Controls.Add(info, 0, 7);
        layout.SetColumnSpan(info, 2);
        Controls.Add(layout);

        trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = Text, Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        menu.Items.Add("Salir", null, (_, _) => Close());
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
        browseButton.Click += (_, _) => BrowseGame();
        installButton.Click += async (_, _) => await InstallAsync();
        startButton.Click += (_, _) => { if (running) Stop(); else Start(); };
        helpLink.LinkClicked += (_, _) =>
        {
            ProcessUrl("https://discord.com/developers/applications");
            MessageBox.Show(this,
                "Crea una aplicación llamada 'ETS2 Discord Presence'. En 'General Information', copia su 'Application ID' y pégalo aquí. Se hace una sola vez.",
                "Configurar Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        timer.Tick += (_, _) => UpdatePresence();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosed += (_, _) => { Stop(); trayIcon.Visible = false; trayIcon.Dispose(); };
        UpdateSetupStatus();
        if (IsValidAppId(appIdBox.Text) && PluginInstaller.IsInstalled(gamePathBox.Text)) Start();
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
            MessageBox.Show(this, "Primero selecciona la carpeta de ETS2.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!PluginInstaller.IsInstalled(gamePathBox.Text.Trim()))
        {
            MessageBox.Show(this, "Primero pulsa 'Instalar componente de ETS2'.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!IsValidAppId(appIdBox.Text.Trim()))
        {
            MessageBox.Show(this, "Pega el ID de tu aplicación de Discord. El enlace de ayuda está arriba.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            SaveSettings();
            discord = new DiscordRpcClient(appIdBox.Text.Trim());
            discord.Initialize();
            running = true;
            startButton.Text = "Detener";
            appIdBox.Enabled = false;
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
        startButton.Text = "Iniciar";
        appIdBox.Enabled = true;
        if (!IsDisposed) statusLabel.Text = "Detenido";
    }

    private void UpdatePresence()
    {
        if (discord is null) return;
        if (!TelemetryReader.TryRead(out var telemetry, out var error) || telemetry is null)
        {
            if (lastPresence is not null) { discord.ClearPresence(); lastPresence = null; }
            statusLabel.Text = error ?? "Esperando ETS2...";
            previewLabel.Text = "La vista previa aparecerá al abrir ETS2.";
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
        previewLabel.Text = $"{presence.Details} — {presence.State}";
    }

    private void UpdateSetupStatus() => statusLabel.Text = SteamLocator.IsValidGamePath(gamePathBox.Text)
        ? "ETS2 encontrado. Instala el componente y configura Discord."
        : "Selecciona la carpeta donde está instalado ETS2.";

    private void SaveSettings() => new AppSettings(appIdBox.Text.Trim(), gamePathBox.Text.Trim()).Save();

    private static bool IsValidAppId(string value) => value.Length is >= 17 and <= 20 && value.All(char.IsAsciiDigit);

    private void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private static void ProcessUrl(string url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
