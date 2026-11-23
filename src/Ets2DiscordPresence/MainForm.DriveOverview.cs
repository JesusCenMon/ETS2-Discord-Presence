namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private TripStrip? currentTripBar;
    private TelemetrySnapshot? displayedDrive;
    private bool driveConnected;
    private bool driveSharing;
    private Label? overviewRoute;
    private Label? overviewDetail;
    private FlowLayoutPanel? overviewMetrics;
    private readonly List<Label> overviewValues = [];

    private void BuildDriveOverview(TableLayoutPanel content)
    {
        var drive = new ShellCard();
        overviewRoute = TextLabel("", 21, true);
        overviewDetail = TextLabel("", 10);
        overviewDetail.Tag = "muted";
        overviewMetrics = ToolBar();
        foreach (var (es, en) in new[] { ("Distancia", "Distance"), ("Llegada", "Arrival"), ("Velocidad", "Speed"), ("Combustible", "Fuel") })
        {
            var tile = Grid(1);
            tile.Dock = DockStyle.None;
            tile.MinimumSize = new Size(124, 72);
            tile.Padding = new Padding(12, 4, 12, 4);
            tile.Margin = new Padding(0, 4, 8, 4);
            var label = TextLabel("", 14, true);
            overviewValues.Add(label);
            AddRows(tile, UiLabel(es, en, 9), label);
            overviewMetrics.Controls.Add(tile);
        }
        AddRows(drive, UiLabel("Viaje actual", "Current drive", 11, true), overviewRoute, overviewDetail, overviewMetrics);
        AddRows(content, drive);
        UpdateDriveOverview(null, null);
        homePage.ClientSizeChanged += (_, _) =>
        {
            var width = homePage.ClientSize.Width - content.Padding.Horizontal - drive.Padding.Horizontal - 24;
            SetWrap(overviewRoute, width);
            SetWrap(overviewDetail, width);
        };
    }

    private string DriveTitle(TelemetrySnapshot trip) =>
        trip.OnJob && !string.IsNullOrWhiteSpace(trip.DestinationCity)
            ? string.IsNullOrWhiteSpace(trip.SourceCity) ? trip.DestinationCity : $"{trip.SourceCity} → {trip.DestinationCity}"
            : L("Conducción libre", "Free driving");

    private void UpdateDriveOverview(TelemetrySnapshot? trip, string? remaining)
    {
        if (overviewRoute is null || overviewDetail is null) return;
        if (homePage.Visible)
        {
            overviewRoute.Text = trip is null ? gameRunning ? L("Esperando una partida", "Waiting for a session")
                : L("ETS2 está cerrado", "ETS2 is closed") : DriveTitle(trip);
            overviewDetail.Text = trip is null ? L("Entra a una partida para ver tu recorrido.", "Enter a session to see your drive.")
                : trip.Paused ? L("En pausa", "Paused") : trip.Cargo;
            overviewDetail.Visible = overviewDetail.Text.Length > 0;
        }
        string?[] values =
        [
            remaining,
            trip?.ArrivalTime,
            trip?.Signals is { Speed: var speed } && float.IsFinite(speed) ? SpeedLabel(Math.Abs(speed) * 3.6) : null,
            trip?.Truck?.FuelPercent is { } fuel ? $"{fuel:N0} %" : null
        ];
        if (homePage.Visible)
        {
            for (var index = 0; index < values.Length; index++)
            {
                var label = overviewValues[index];
                label.Text = values[index] ?? "";
                label.Parent!.Visible = values[index] is not null;
            }
            overviewMetrics!.Visible = values.Any(value => value is not null);
        }
        if (!tripShellPage.Visible) return;
        void Display(Label label, string? value)
        {
            label.Text = value ?? "";
            if (label.Parent is { } tile) tile.Visible = value is not null;
        }
        Display(tripSpeed, values[2]);
        Display(tripFuel, values[3]);
        if (distance.Parent?.Parent is { } distanceTile) distanceTile.Visible = values[0] is not null;
        if (arrival.Parent is { } arrivalTile) arrivalTile.Visible = values[1] is not null;
    }

    private TripStrip CreateTripBar(Action open)
    {
        var strip = new TripStrip { BackColor = shellPalette.Elevated, Visible = false };
        strip.Open.Click += (_, _) => open();
        strip.Metadata.Cursor = strip.Progress.Cursor = Cursors.Hand;
        strip.Metadata.Click += (_, _) => open();
        strip.Progress.Click += (_, _) => open();
        return strip;
    }

    private void RenderDriveOverview(TelemetrySnapshot? trip, bool connected, bool sharing)
    {
        displayedDrive = trip;
        driveConnected = connected;
        driveSharing = sharing;
        if (!Visible || UiBusy) return;
        var remaining = trip?.RemainingKm is { } km ? AppOptions.Distance(km, preferences.Features.Miles) : null;
        if (homePage.Visible || tripShellPage.Visible) UpdateDriveOverview(trip, remaining);
        var discord = !connected ? L("○ Discord desconectado", "○ Discord offline")
            : sharing ? L("● Discord activo", "● Discord active") : L("Ⅱ Discord en pausa", "Ⅱ Discord paused");
        foreach (var strip in new[] { currentTripBar })
        {
            if (strip is null || strip.IsDisposed) continue;
            strip.Visible = trip is not null && currentPage is not ("home" or "trip")
                && !(currentPage == "settings" && ClientSize.Height / (DeviceDpi / 96f) < 620);
            if (trip is null) continue;
            var title = DriveTitle(trip);
            strip.Open.Text = title;
            strip.Open.AccessibleName = L($"Abrir viaje · {title}", $"Open trip · {title}");
            strip.Metadata.Text = string.Join("   ·   ", new[] { trip.Cargo, remaining, discord }.Where(value => !string.IsNullOrWhiteSpace(value)));
            FullStripHint(strip, title);
            strip.Metadata.Visible = !(ClientSize.Height / (DeviceDpi / 96f) < 620 && preferences.TextPercent > 135);
            strip.Metadata.ForeColor = connected && sharing ? shellPalette.Discord : shellPalette.Muted;
            strip.Progress.Palette = shellPalette;
            strip.Progress.SetValue(trip.Progress ?? 0, Motion);
            strip.Progress.Visible = trip.Progress.HasValue;
            strip.Progress.AccessibleName = L($"Avance {trip.Progress} por ciento", $"Progress {trip.Progress} percent");
        }
    }

    private void FullStripHint(TripStrip strip, string title)
    {
        shellTips.SetToolTip(strip.Open, title);
        shellTips.SetToolTip(strip.Metadata, strip.Metadata.Text);
    }

    private sealed class TripStrip : TableLayoutPanel
    {
        internal readonly Button Open = MakeButton("");
        internal readonly Label Metadata = TextLabel("", 9);
        internal readonly RouteProgress Progress = new();

        internal TripStrip()
        {
            DoubleBuffered = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Bottom;
            ColumnCount = 1;
            Padding = new Padding(16, 4, 16, 8);
            Margin = Padding.Empty;
            Open.Tag = "quiet";
            Open.Dock = DockStyle.Top;
            Open.AutoEllipsis = true;
            Open.AutoSize = false;
            Open.Height = 38;
            Open.TextAlign = ContentAlignment.MiddleLeft;
            Metadata.AutoSize = false;
            Metadata.Height = 25;
            Metadata.AutoEllipsis = true;
            AddRows(this, Open, Metadata, Progress);
            void FitText()
            {
                Open.Height = Math.Max(38 * DeviceDpi / 96, Open.Font.Height + Open.Padding.Vertical + 12);
                Metadata.Height = Metadata.Font.Height + 8;
            }
            Open.FontChanged += (_, _) => FitText();
            Metadata.FontChanged += (_, _) => FitText();
            DpiChangedAfterParent += (_, _) => FitText();
            FitText();
        }
    }
}
