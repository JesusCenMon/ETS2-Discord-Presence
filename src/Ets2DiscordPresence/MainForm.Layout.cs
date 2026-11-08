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
    private readonly TableLayoutPanel travel = Grid(1);
    private readonly TableLayoutPanel preview = Grid(1);
    private readonly TableLayoutPanel route = Grid(3);
    private readonly TableLayoutPanel progressArea = Grid(1);
    private readonly TableLayoutPanel connections = Grid(2);
    private readonly RoadArtwork artwork = new();
    private bool? stacked;
    private bool? compactLayout;

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
        header.Controls.Add(settingsButton, 1, 0);
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
        AddRows(travel, travelMode, cargo);
        route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6));
        route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        route.Margin = new Padding(0, 12, 0, 8);
        route.Controls.Add(Metric("ORIGEN", origin), 0, 0);
        var arrow = TextLabel("→", 13, true);
        arrow.TextAlign = ContentAlignment.MiddleCenter;
        route.Controls.Add(arrow, 1, 0);
        route.Controls.Add(Metric("DESTINO", destination), 2, 0);
        AddRows(travel, route, cityLabel);

        var metrics = Grid(2);
        metrics.Margin = new Padding(0, 13, 0, 0);
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metrics.Controls.Add(Metric("DISTANCIA RESTANTE", distance), 0, 0);
        metrics.Controls.Add(Metric("LLEGADA · HORA DEL JUEGO", arrival), 1, 0);
        AddRows(travel, metrics);
        progressArea.Margin = new Padding(0, 8, 0, 0);
        AddRows(progressArea, progressCaption, progress);
        AddRows(travel, progressArea);

        preview.BackColor = Surface;
        preview.Padding = new Padding(16, 10, 16, 9);
        preview.Margin = new Padding(0, 0, 0, 12);
        var previewTitle = TextLabel("VISTA PREVIA EN DISCORD", 9, true);
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
        actions.Controls.AddRange([toggle, launch, locate, updateButton]);
        locate.Visible = false;
        updateButton.Visible = false;

        var panel = Grid(1);
        panel.Name = "tripPanel";
        AddRows(panel, travel, connections, guidance, actions, preview);
        body.Controls.Add(panel, 0, 0);

        artwork.Dock = DockStyle.Fill;
        artwork.Margin = new Padding(14, 0, 0, 0);
        artwork.MinimumSize = new Size(0, 145);
        body.Controls.Add(artwork, 1, 0);
        scroll.ClientSizeChanged += (_, _) => ArrangeLayout();
        ArrangeLayout();
    }

    private void ArrangeLayout()
    {
        if (body.Controls.Count < 2) return;
        var scale = DeviceDpi / 96f;
        var compact = preferences.Compact;
        var narrow = scroll.ClientSize.Width / scale < 790;
        if (stacked != narrow || compactLayout != compact)
        {
            stacked = narrow;
            compactLayout = compact;
            body.SuspendLayout();
            body.ColumnStyles[0].Width = narrow || compact ? 100 : 70;
            body.ColumnStyles[1].Width = narrow || compact ? 0 : 30;
            body.SetCellPosition(artwork, narrow || compact ? new TableLayoutPanelCellPosition(0, 1) : new(1, 0));
            artwork.Dock = narrow ? DockStyle.Top : DockStyle.Fill;
            artwork.Height = (int)(150 * scale);
            artwork.Margin = narrow ? new Padding(0, 4, 0, 0) : new Padding(14, 0, 0, 0);
            artwork.Visible = !compact;
            preview.Visible = !compact;
            body.ResumeLayout(true);
        }

        var width = Math.Max(220, scroll.ClientSize.Width - body.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        if (!narrow && !compact) width = (int)(width * .70);
        SetWrap(cargo, width - travel.Padding.Horizontal);
        SetWrap(cityLabel, width - travel.Padding.Horizontal);
        SetWrap(origin, (int)((width - travel.Padding.Horizontal) * .45));
        SetWrap(destination, (int)((width - travel.Padding.Horizontal) * .45));
        SetWrap(state, width - preview.Padding.Horizontal);
        SetWrap(details, width - preview.Padding.Horizontal);
        SetWrap(guidance, width);
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
        travelMode.Text = trip is null ? "SIN VIAJE ACTIVO" : trip.Paused ? "JUEGO EN PAUSA"
            : trip.OnJob ? "ENTREGA EN CURSO" : "CONDUCCIÓN LIBRE";
        travelMode.ForeColor = trip is null || trip.Paused ? Amber : Mint;
        cargo.Text = trip is null ? "Esperando una partida"
            : trip.OnJob ? (string.IsNullOrWhiteSpace(trip.Cargo) ? "Entrega sin nombre de carga" : trip.Cargo)
            : "Explorando el mapa";
        route.Visible = trip?.OnJob == true;
        origin.Text = string.IsNullOrWhiteSpace(trip?.SourceCity) ? "—" : trip.SourceCity;
        destination.Text = string.IsNullOrWhiteSpace(trip?.DestinationCity) ? "—" : trip.DestinationCity;
        cityLabel.Text = nearby is null ? "Ubicación aproximada · No disponible" : $"Ubicación aproximada · Cerca de {nearby}";
        distance.Text = trip?.RemainingKm is { } km ? $"{Math.Ceiling(km):N0} km" : "—";
        arrival.Text = trip?.ArrivalTime ?? "—";
        var percent = trip?.Progress;
        progressArea.Visible = percent.HasValue;
        progress.SetValue(percent ?? 0);
        progressCaption.Text = percent.HasValue ? $"Progreso estimado · {percent} %" : "";
        previewName.Text = demo ? "ETS2 Presence · Ejemplo local" : "ETS2 Presence";
        details.Text = demo || preferences.Sharing ? presence?.Details ?? "Sin actividad para publicar" : "Publicación en pausa";
        state.Text = demo || preferences.Sharing ? presence?.State ?? "Entra a una partida para ver la actividad."
            : "El viaje se sigue mostrando en el panel.";
    }

    private static TableLayoutPanel Metric(string caption, Label value)
    {
        var metric = Grid(1);
        AddRows(metric, TextLabel(caption, 9, true), value);
        return metric;
    }

    private static void SetStatus(Label label, string text, bool active)
    {
        label.Text = $"●  {text}";
        label.ForeColor = active ? Mint : Amber;
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
            e.Graphics.Clear(Background);
            using var fill = new SolidBrush(Mint);
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
