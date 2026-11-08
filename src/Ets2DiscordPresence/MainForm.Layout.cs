using System.Drawing.Drawing2D;
using Button = System.Windows.Forms.Button;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private static readonly Color Background = Color.FromArgb(12, 21, 27);
    private static readonly Color Surface = Color.FromArgb(21, 35, 43);
    private static readonly Color Foreground = Color.FromArgb(247, 250, 249);
    private static readonly Color Muted = Color.FromArgb(200, 216, 219);
    private static readonly Color Mint = Color.FromArgb(128, 229, 201);
    private static readonly Color Amber = Color.FromArgb(245, 192, 117);

    private readonly Label gameStatus = TextLabel("ETS2 · Buscando…", 10);
    private readonly Label discordStatus = TextLabel("Discord · Buscando…", 10);
    private readonly Label travelMode = TextLabel("SIN VIAJE ACTIVO", 10, true);
    private readonly Label cargo = TextLabel("Esperando una partida", 21, true);
    private readonly Label origin = TextLabel("—", 13, true);
    private readonly Label destination = TextLabel("—", 13, true);
    private readonly Label cityLabel = TextLabel("Ubicación aproximada · No disponible", 10);
    private readonly Label distance = TextLabel("—", 23, true);
    private readonly Label arrival = TextLabel("—", 15, true);
    private readonly Label progressCaption = TextLabel("", 10);
    private readonly Label details = TextLabel("Sin actividad para publicar", 12, true);
    private readonly Label state = TextLabel("", 10);
    private readonly Label guidance = TextLabel("Preparando la conexión…", 10);
    private readonly Label previewName = TextLabel("ETS2 Presence", 10, true);
    private readonly TripProgress progress = new() { Dock = DockStyle.Top, Height = 9 };
    private readonly Button toggle = MakeButton("Pausar publicación");
    private readonly Button locate = MakeButton("Buscar ETS2");
    private readonly Button launch = MakeButton("▶  Abrir ETS2");
    private readonly Button settingsButton = MakeButton("⚙  Ajustes");
    private readonly Button updateButton = MakeButton("↓  Descargar actualización");
    private readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly TableLayoutPanel body = Grid(2);
    private readonly TableLayoutPanel side = Grid(1);
    private readonly TableLayoutPanel tripPanel = Grid(1);
    private readonly TableLayoutPanel travel = Grid(1);
    private readonly TableLayoutPanel preview = Grid(1);
    private readonly TableLayoutPanel route = Grid(3);
    private readonly TableLayoutPanel progressArea = Grid(1);
    private readonly TableLayoutPanel metrics = Grid(2);
    private readonly TableLayoutPanel connections = Grid(2);
    private readonly RoadArtwork artwork = new();
    private bool? stacked;
    private bool? compactLayout;
    private bool? artworkVisible;

    private void BuildLayout()
    {
        var shell = Grid(1);
        shell.AutoSize = false;
        shell.Dock = DockStyle.Fill;
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var header = Grid(2);
        header.Padding = new Padding(18, 13, 18, 10);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(TextLabel("ETS2 Discord Presence", 17, true), 0, 0);
        header.Controls.Add(Translate(settingsButton, "⚙ Ajustes", "⚙ Settings"), 1, 0);
        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(scroll, 0, 1);

        body.Padding = new Padding(18, 0, 18, 14);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        scroll.Controls.Add(body);

        travel.BackColor = Surface;
        travel.Padding = new Padding(18, 14, 18, 14);
        travel.Margin = new Padding(0, 0, 0, 12);
        travelMode.ForeColor = Mint;
        AddRows(travel, travelMode, cargo, timeline);
        route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6));
        route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        route.Margin = new Padding(0, 12, 0, 8);
        route.Controls.Add(Metric("ORIGEN", "ORIGIN", origin), 0, 0);
        var arrow = TextLabel("→", 13, true);
        arrow.TextAlign = ContentAlignment.MiddleCenter;
        route.Controls.Add(arrow, 1, 0);
        route.Controls.Add(Metric("DESTINO", "DESTINATION", destination), 2, 0);
        AddRows(travel, route, cityLabel);

        metrics.Margin = new Padding(0, 13, 0, 0);
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.Controls.Add(Metric("DISTANCIA RESTANTE", "REMAINING DISTANCE", distance), 0, 0);
        metrics.Controls.Add(Metric("LLEGADA · HORA DEL JUEGO", "ARRIVAL · GAME TIME", arrival), 1, 0);
        AddRows(travel, metrics);
        progressArea.Margin = new Padding(0, 8, 0, 0);
        AddRows(progressArea, progressCaption, progress);
        AddRows(travel, progressArea, deadlineAlert);

        preview.BackColor = Surface;
        preview.Padding = new Padding(16, 10, 16, 9);
        preview.Margin = new Padding(0, 0, 0, 12);
        var previewTitle = UiLabel("VISTA PREVIA EN DISCORD", "DISCORD PREVIEW", 9, true);
        previewTitle.ForeColor = Mint;
        AddRows(preview, previewTitle, previewName, details, state);

        connections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        connections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        connections.Margin = new Padding(0, 0, 0, 7);
        gameStatus.BackColor = Surface;
        discordStatus.BackColor = Surface;
        gameStatus.Padding = new Padding(9, 4, 9, 4);
        discordStatus.Padding = gameStatus.Padding;
        gameStatus.Margin = new Padding(0, 0, 5, 0);
        discordStatus.Margin = new Padding(5, 0, 0, 0);
        connections.Controls.Add(gameStatus, 0, 0);
        connections.Controls.Add(discordStatus, 1, 0);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        actions.Controls.AddRange([toggle, Translate(launch, "▶ Abrir ETS2", "▶ Launch ETS2"),
            Translate(locate, "Buscar ETS2", "Locate ETS2"), updateButton,
            Translate(historyButton, "Historial", "History"), Translate(monitorButton, "Segundo monitor", "Second monitor"),
            Translate(summaryButton, "Ver resumen", "View summary"), Translate(dismissGuide, "Ocultar guía", "Hide guide")]);
        historyButton.Click += (_, _) => ShowHistory();
        monitorButton.Click += (_, _) => ToggleMonitor();
        summaryButton.Click += (_, _) => { if (completedTrip is { } entry) ShowSummary(entry, this); };
        dismissGuide.Click += (_, _) => SavePreferences(preferences with { GuideDismissed = true });
        locate.Visible = false;
        updateButton.Visible = false;

        AddRows(tripPanel, travel, connections, guidance, onboarding, actions, preview, tripMap, finishBanner);
        panelSections.Add("trip", travel);
        panelSections.Add("discord", preview);
        panelSections.Add("map", tripMap);
        panelSections.Add("art", artwork);
        body.Controls.Add(tripPanel, 0, 0);

        artwork.Dock = DockStyle.Top;
        artwork.Margin = new Padding(0, 10, 0, 0);
        artwork.MinimumSize = new Size(0, 145);
        side.Margin = new Padding(14, 0, 0, 0);
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.Controls.Add(artwork, 0, 1);
        body.Controls.Add(side, 1, 0);
        scroll.ClientSizeChanged += (_, _) => ArrangeLayout();
        ArrangeLayout();
    }

    private void ArrangeLayout()
    {
        if (body.Controls.Count < 2) return;
        var scale = DeviceDpi / 96f;
        var compact = preferences.Compact && !fullScreen;
        var showArtwork = !compact && !fullScreen && preferences.HiddenPanels?.Contains("art") != true;
        var narrow = scroll.ClientSize.Width / scale < 790;
        var mapAtSide = !narrow && !compact && !fullScreen && preferences.PanelOrder is null
            && preferences.HiddenPanels?.Contains("map") != true;
        var hasSide = showArtwork || mapAtSide;
        var mapWasAtSide = tripMap.Parent == side;
        if (stacked != narrow || compactLayout != compact || artworkVisible != hasSide || mapWasAtSide != mapAtSide)
        {
            stacked = narrow;
            compactLayout = compact;
            artworkVisible = hasSide;
            body.SuspendLayout();
            body.ColumnStyles[0].Width = narrow || !hasSide ? 100 : 67;
            body.ColumnStyles[1].Width = narrow || !hasSide ? 0 : 33;
            body.SetCellPosition(side, narrow || !hasSide ? new TableLayoutPanelCellPosition(0, 1) : new(1, 0));
            side.Margin = narrow ? new Padding(0, 4, 0, 0) : new Padding(14, 0, 0, 0);
            artwork.Height = (int)((narrow ? 150 : 285) * scale);
            side.Visible = hasSide;
            if (mapAtSide)
            {
                side.Controls.Add(tripMap, 0, 0);
            }
            else if (tripMap.Parent == side)
            {
                tripPanel.Controls.Add(tripMap, 0, 6);
            }
            OrderTripPanels();
            body.ResumeLayout(true);
        }
        artwork.Visible = showArtwork;

        var width = Math.Max(220, scroll.ClientSize.Width - body.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        if (!narrow && hasSide) width = (int)(width * .67);
        SetWrap(cargo, width - travel.Padding.Horizontal);
        SetWrap(cityLabel, width - travel.Padding.Horizontal);
        SetWrap(origin, (int)((width - travel.Padding.Horizontal) * .45));
        SetWrap(destination, (int)((width - travel.Padding.Horizontal) * .45));
        SetWrap(state, width - preview.Padding.Horizontal);
        SetWrap(details, width - preview.Padding.Horizontal);
        SetWrap(guidance, width);
        SetWrap(onboarding, width);
        SetWrap(timeline, width - travel.Padding.Horizontal);
        SetWrap(deadlineAlert, width - travel.Padding.Horizontal);
        SetWrap(finishBanner, width);
        SetWrap(discordStatus, width / 2 - 12);
        SetWrap(gameStatus, width / 2 - 12);
    }

    private static void SetWrap(Label label, int width)
    {
        var maximum = new Size(Math.Max(70, width), 0);
        if (label.MaximumSize != maximum) label.MaximumSize = maximum;
    }

    private void ApplyCompactSize()
    {
        var scale = DeviceDpi / 96f;
        ClientSize = preferences.Compact
            ? new Size((int)(465 * scale), (int)(450 * scale))
            : new Size((int)(1070 * scale), (int)(710 * scale));
        ArrangeLayout();
    }

    private void RenderTrip(TelemetrySnapshot? trip, string? nearby, PresenceText? presence)
    {
        travelMode.Text = trip is null ? L("SIN VIAJE ACTIVO", "NO ACTIVE TRIP") : trip.Paused ? L("JUEGO EN PAUSA", "GAME PAUSED")
            : trip.OnJob ? L("ENTREGA EN CURSO", "DELIVERY IN PROGRESS") : L("CONDUCCIÓN LIBRE", "FREE ROAM");
        travelMode.ForeColor = Themed(trip is null || trip.Paused ? Amber : Mint);
        cargo.Text = trip is null ? L("Esperando una partida", "Waiting for a game session")
            : trip.OnJob ? (string.IsNullOrWhiteSpace(trip.Cargo) ? L("Entrega sin nombre de carga", "Delivery without cargo name") : trip.Cargo)
            : L("Explorando el mapa", "Exploring the map");
        cargo.Visible = preferences.HiddenPanels?.Contains("cargo") != true;
        route.Visible = trip?.OnJob == true && preferences.HiddenPanels?.Contains("route") != true;
        cityLabel.Visible = preferences.HiddenPanels?.Contains("location") != true;
        metrics.Visible = preferences.HiddenPanels?.Contains("metrics") != true;
        origin.Text = string.IsNullOrWhiteSpace(trip?.SourceCity) ? "—" : trip.SourceCity;
        destination.Text = string.IsNullOrWhiteSpace(trip?.DestinationCity) ? "—" : trip.DestinationCity;
        cityLabel.Text = nearby is null ? L("Ubicación aproximada · No disponible", "Approximate location · Unavailable") : L($"Ubicación aproximada · Cerca de {nearby}", $"Approximate location · Near {nearby}");
        distance.Text = trip?.RemainingKm is { } km ? $"{Math.Ceiling(km):N0} km" : "—";
        arrival.Text = trip?.ArrivalTime ?? "—";
        var percent = trip?.Progress;
        progressArea.Visible = percent.HasValue && preferences.HiddenPanels?.Contains("metrics") != true;
        progress.SetValue(percent ?? 0);
        progressCaption.Text = percent.HasValue ? L($"Progreso estimado · {percent} %", $"Estimated progress · {percent}%") : "";
        previewName.Text = demo ? L("ETS2 Presence · Ejemplo local", "ETS2 Presence · Local example") : "ETS2 Presence";
        details.Text = demo || preferences.Sharing ? presence?.Details ?? L("Sin actividad para publicar", "No activity to publish") : L("Publicación en pausa", "Publishing paused");
        state.Text = demo || preferences.Sharing ? presence?.State ?? L("Entra a una partida para ver la actividad.", "Enter a game session to see the activity.")
            : L("El viaje se sigue mostrando en el panel.", "The trip is still visible on the dashboard.");
    }

    private TableLayoutPanel Metric(string caption, string translation, Label value)
    {
        var metric = Grid(1);
        AddRows(metric, UiLabel(caption, translation, 9, true), value);
        return metric;
    }

    private void SetStatus(Label label, string text, bool active)
    {
        label.Text = $"●  {text}";
        label.ForeColor = Themed(active ? Mint : Amber);
    }

    private static TableLayoutPanel Grid(int columns) => new()
    {
        Dock = DockStyle.Top, ColumnCount = columns, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
    };

    private static void AddRows(TableLayoutPanel grid, params Control[] rows)
    {
        foreach (var control in rows)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(control, 0, row);
        }
    }

    private static Label TextLabel(string text, int size, bool bold = false) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2),
        AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = bold ? Foreground : Muted,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    private static Button MakeButton(string text)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(82, 35),
            Margin = new Padding(0, 4, 7, 4), FlatStyle = FlatStyle.Flat,
            BackColor = Surface, ForeColor = Foreground,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(78, 112, 118);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(34, 61, 68);
        button.GotFocus += (_, _) => button.FlatAppearance.BorderColor = Mint;
        button.LostFocus += (_, _) => button.FlatAppearance.BorderColor = Color.FromArgb(78, 112, 118);
        return button;
    }

    private sealed class TripProgress : Control
    {
        private int value;

        public void SetValue(int percent)
        {
            if (value == percent) return;
            value = percent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var form = FindForm() as MainForm;
            e.Graphics.Clear(form?.Themed(Background) ?? Background);
            using var fill = new SolidBrush(form?.Themed(Mint) ?? Mint);
            e.Graphics.FillRectangle(fill, 0, 0, Width * value / 100f, Height);
        }
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
            AccessibleName = "Ilustración de un camión cruzando un puente";
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image.Dispose();
            base.Dispose(disposing);
        }
    }
}
