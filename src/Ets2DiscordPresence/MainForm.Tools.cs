using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private const int PrivacyHotkeyId = 81;
    private bool privateDestination;
    private bool hotkeyRegistered;
    private bool pauseHotkeyRegistered;
    private bool fuelAlerted;
    private bool liveDelivery;
    private bool retentionChecked;
    private string? fuelTruck;
    private string? readerError;
    private readonly Label truckName = TextLabel("", 12, true);
    private readonly VehicleMark vehicleMark = new();
    private readonly Label truckStats = TextLabel("", 10);
    private readonly Label fuelStatus = TextLabel("", 10, true);
    private readonly ProgressBar fuelLevel = new() { Dock = DockStyle.Top, Height = 8, TabStop = false };
    private readonly ToolStripMenuItem trayQuick = new();
    private Form? quickWindow;
    private Form? captureWindow;
    private Label? quickText;
    private PictureBox? quickArtwork;
    private Label? captureRoute;
    private Label? captureDistance;
    private readonly ConditionalWeakTable<Control, TextSize> textSizes = new();

    private sealed class TextSize(Font baseline)
    {
        public Font Baseline { get; } = baseline;
        public Font? Applied { get; set; }
    }

    private Form ToolWindow(string title, Size size, Form? target = null, string? placement = null)
    {
        var window = target ?? new Form();
        window.Text = title;
        window.ClientSize = size;
        window.MinimumSize = new Size(Math.Min(size.Width, 520), Math.Min(size.Height, 360));
        window.StartPosition = FormStartPosition.CenterParent;
        window.BackColor = Background;
        window.ForeColor = Foreground;
        window.AutoScaleMode = AutoScaleMode.Dpi;
        window.Icon = appIcon;
        if (placement is not null) RememberWindow(window, placement);
        return window;
    }

    private sealed record WindowPlacement(string Monitor, Rectangle LogicalBounds, bool Maximized);

    private static void RememberWindow(Form window, string key)
    {
        var path = Path.Combine(LocalData.Folder, $"placement-{key}.json");
        window.Load += (_, _) =>
        {
            try
            {
                if (!File.Exists(path) || System.Text.Json.JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path)) is not { } saved) return;
                var monitor = Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == saved.Monitor) ?? Screen.FromControl(window);
                var work = monitor.WorkingArea;
                window.Location = new Point(work.X + 16, work.Y + 16);
                var scale = window.DeviceDpi / 96d;
                var logical = saved.LogicalBounds;
                int Physical(int value) => (int)Math.Round(Math.Clamp(value, -20000, 20000) * scale);
                var width = Math.Clamp(Physical(logical.Width), Math.Min(window.MinimumSize.Width, work.Width), work.Width);
                var height = Math.Clamp(Physical(logical.Height), Math.Min(window.MinimumSize.Height, work.Height), work.Height);
                window.Bounds = new Rectangle(Math.Clamp(work.X + Physical(logical.X), work.Left, work.Right - width),
                    Math.Clamp(work.Y + Physical(logical.Y), work.Top, work.Bottom - height), width, height);
                if (saved.Maximized && window.WindowState != FormWindowState.Minimized) window.WindowState = FormWindowState.Maximized;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        };
        window.FormClosed += (_, _) =>
        {
            try
            {
                var bounds = window.WindowState == FormWindowState.Normal ? window.Bounds : window.RestoreBounds;
                var monitor = Screen.FromRectangle(bounds);
                var work = monitor.WorkingArea;
                var scale = 96d / window.DeviceDpi;
                int Logical(int value) => (int)Math.Round(value * scale);
                var saved = new WindowPlacement(monitor.DeviceName, new Rectangle(Logical(bounds.X - work.X),
                    Logical(bounds.Y - work.Y), Logical(bounds.Width), Logical(bounds.Height)), window.WindowState == FormWindowState.Maximized);
                LocalData.WriteFile(path, System.Text.Json.JsonSerializer.Serialize(saved));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
    }

    private static FlowLayoutPanel ToolBar() => new()
    {
        Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty
    };

    private void StyleTool(Control window)
    {
        Restyle(window);
        ScaleText(window);
    }

    private void ScaleText(Control control)
    {
        if (control is Label or Button or CheckBox or TextBox or ListBox or DataGridView or ComboBox or TabControl or DateTimePicker or TripMap)
        {
            var baseline = textSizes.GetValue(control, child =>
            {
                var state = new TextSize(child.Font);
                child.Disposed += (_, _) => state.Applied?.Dispose();
                return state;
            });
            var percent = control is DataGridView or ListBox
                ? Math.Clamp(preferences.Features.TableTextPercent, 100, 150)
                : Math.Clamp(preferences.TextPercent, 85, 150);
            var original = control == cargo && fullScreen ? 34 : control == distance && fullScreen ? 42 : baseline.Baseline.SizeInPoints;
            var target = original * percent / 100;
            if (Math.Abs(control.Font.SizeInPoints - target) > .02f)
            {
                var replacement = new Font(baseline.Baseline.FontFamily, target, baseline.Baseline.Style);
                control.Font = replacement;
                baseline.Applied?.Dispose();
                baseline.Applied = replacement;
            }
            if (control is DataGridView grid)
                grid.RowTemplate.Height = Math.Max(22, (int)Math.Ceiling(grid.Font.GetHeight()) + 10);
        }
        foreach (Control child in control.Controls) ScaleText(child);
    }

    private FlowLayoutPanel MapControls(TripMap map, bool translate = false)
    {
        var bar = ToolBar();
        var smaller = MakeButton("−");
        var larger = MakeButton("+");
        var center = MakeButton(L("Centrar", "Recenter"));
        smaller.AccessibleName = L("Alejar mapa", "Zoom out");
        larger.AccessibleName = L("Acercar mapa", "Zoom in");
        if (translate) Translate(center, "Centrar", "Recenter");
        smaller.Click += (_, _) => map.Zoom(.8);
        larger.Click += (_, _) => map.Zoom(1.25);
        center.Click += (_, _) => map.Recenter();
        var more = MakeButton(L("Mapa…", "Map…"));
        if (translate) Translate(more, "Mapa…", "Map…");
        var menu = new ContextMenuStrip();
        more.Disposed += (_, _) => menu.Dispose();
        menu.Items.Add(L("Ver recorrido completo", "Fit full trip"), null, (_, _) => map.FitTrail());
        menu.Items.Add(L("Buscar ciudad…", "Find city…"), null, (_, _) => SearchMapCity(map));
        foreach (var (key, title, initial) in new[]
        {
            ("heading", L("Rumbo arriba", "Heading up"), false), ("follow", L("Seguir vehículo", "Follow vehicle"), false),
            ("cities", L("Ciudades", "Cities"), true), ("track", L("Recorrido", "Track"), true),
            ("terminals", L("Origen y destino", "Origin and destination"), true), ("events", L("Eventos", "Events"), true),
            ("speed", L("Velocidad por color · naranja = lento", "Color by speed · orange = slow"), false)
        })
        {
            var item = new ToolStripMenuItem(title) { CheckOnClick = true, Checked = initial };
            item.CheckedChanged += (_, _) => map.ConfigureLayer(key, item.Checked);
            menu.Items.Add(item);
        }
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        var legend = MakeButton(L("Leyenda", "Legend"));
        var legendText = TextLabel(L("● Ciudad   ▪ Parada   → Vehículo   ◆ Evento   ━ Recorrido   ┄ Dirección al destino",
            "● City   ▪ Stop   → Vehicle   ◆ Event   ━ Track   ┄ Direction to destination"), 9);
        legendText.Visible = false;
        legend.Click += (_, _) =>
        {
            legendText.Visible = !legendText.Visible;
            legend.Text = legendText.Visible ? L("Ocultar leyenda", "Hide legend") : L("Leyenda", "Legend");
        };
        bar.Controls.AddRange([smaller, larger, center, more, legend, legendText]);
        return bar;
    }

    private void SearchMapCity(TripMap map)
    {
        using var window = ToolWindow(L("Buscar ciudad", "Find city"), new Size(470, 420));
        var query = new TextBox { Dock = DockStyle.Top, PlaceholderText = L("Escribe el nombre", "Enter a city name") };
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = nameof(MapCity.Name) };
        void RefreshMatches()
        {
            list.Items.Clear();
            list.Items.AddRange(cities.Cities.Where(city => city.Name.Contains(query.Text.Trim(),
                StringComparison.CurrentCultureIgnoreCase)).OrderBy(city => city.Name).Take(100).Cast<object>().ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        query.TextChanged += (_, _) => RefreshMatches();
        var choose = MakeButton(L("Centrar en la ciudad", "Center on city"));
        choose.Dock = DockStyle.Bottom;
        choose.Click += (_, _) =>
        {
            if (list.SelectedItem is not MapCity city) return;
            map.FocusCity(city);
            window.Close();
        };
        list.DoubleClick += (_, _) => choose.PerformClick();
        window.AcceptButton = choose;
        window.Controls.Add(list);
        window.Controls.Add(choose);
        window.Controls.Add(query);
        RefreshMatches();
        StyleTool(window);
        window.ShowDialog(map.FindForm() ?? this);
    }

    private bool Confirm(IWin32Window owner, string text) =>
        MessageBox.Show(owner, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private bool DataAction(IWin32Window owner, Action action)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            MessageBox.Show(owner, L("No se pudo completar la operación con este archivo o carpeta.",
                "The operation could not be completed with this file or folder."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private string DestinationPrivacyLabel() => privateDestination ? L("Mostrar destino en Discord", "Show destination on Discord")
        : L("Ocultar destino en Discord", "Hide destination on Discord");

    private void ToggleDestinationPrivacy()
    {
        privateDestination = !privateDestination;
        publishImmediately = true;
        RefreshPresence();
    }

    private void RegisterPrivacyShortcut()
    {
        if (!IsHandleCreated) return;
        if (hotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId);
        if (pauseHotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId + 1);
        pauseHotkeyRegistered = preferences.Features.PauseShortcut && RegisterHotKey(Handle, PrivacyHotkeyId + 1, 0x4003, 0x7A);
        if (markerHotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId + 2);
        markerHotkeyRegistered = recording is not null && RegisterHotKey(Handle, PrivacyHotkeyId + 2, 0x4003, 0x7B);
        var key = preferences.PrivacyShortcut;
        hotkeyRegistered = key is >= 1 and <= 3 && RegisterHotKey(Handle, PrivacyHotkeyId, 0x4003, (uint)(0x76 + key));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterPrivacyShortcut();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId);
        if (pauseHotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId + 1);
        if (markerHotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId + 2);
        hotkeyRegistered = pauseHotkeyRegistered = markerHotkeyRegistered = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == PrivacyHotkeyId + 2)
        {
            AddRecordingMarker();
            return;
        }
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == PrivacyHotkeyId + 1)
        {
            ToggleSharing();
            return;
        }
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == PrivacyHotkeyId)
        {
            ToggleDestinationPrivacy();
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    private void ShowTrayQuickPanel()
    {
        if (quickWindow is { IsDisposed: false }) { quickWindow.Activate(); return; }
        var window = ToolWindow(L("Viaje rápido", "Quick trip"), new Size(390, 185));
        window.MinimumSize = Size.Empty;
        window.ShowInTaskbar = false;
        window.TopMost = true;
        window.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        window.StartPosition = FormStartPosition.CenterScreen;
        var root = Grid(1);
        root.Padding = new Padding(12);
        var bar = ToolBar();
        var dashboard = MakeButton(L("Abrir panel", "Open dashboard"));
        var privacy = MakeButton(L("Cambiar privacidad", "Toggle privacy"));
        bar.Controls.AddRange([dashboard, privacy]);
        quickWindow = window;
        var label = TextLabel("", 12, true);
        quickText = label;
        var cover = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, TabStop = false,
            AccessibleName = L("Portada de Discord", "Discord artwork") };
        quickArtwork = cover;
        label.MaximumSize = new Size(270, 0);
        var current = ToolBar();
        current.Controls.AddRange([cover, label]);
        dashboard.Click += (_, _) => { window.Close(); RestoreWindow(); };
        privacy.Click += (_, _) => ToggleDestinationPrivacy();
        AddRows(root, TextLabel(L("VIAJE ACTUAL", "CURRENT TRIP"), 10, true), current, bar);
        window.Controls.Add(root);
        if (preferences.Features.FloatingBounds is { } bounds
            && Screen.AllScreens.Any(screen => Rectangle.Intersect(screen.WorkingArea, bounds).Width >= 100))
        {
            window.StartPosition = FormStartPosition.Manual;
            window.Bounds = bounds;
        }
        window.FormClosed += (_, _) =>
        {
            quickText = null;
            quickArtwork = null;
            quickWindow = null;
            if (!Disposing && !IsDisposed && window.WindowState == FormWindowState.Normal)
            {
                preferences = preferences with { Options = preferences.Features with { FloatingBounds = window.Bounds } };
                try { preferences.Save(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        };
        StyleTool(window);
        RefreshPresence();
        SyncHomeArt();
        window.Show();
    }

    private void ShowCaptureCard()
    {
        if (captureWindow is { IsDisposed: false }) { captureWindow.Activate(); return; }
        var scale = Math.Clamp(preferences.Features.ObsSize, 75, 150) / 100d;
        var window = ToolWindow("ETS2 · OBS", new Size((int)(560 * scale), (int)(155 * scale)));
        window.MinimumSize = new Size(400, 140);
        window.FormBorderStyle = FormBorderStyle.SizableToolWindow;
        window.BackColor = Color.Fuchsia;
        window.TransparencyKey = Color.Fuchsia;
        var card = Grid(1);
        card.BackColor = Background;
        card.ForeColor = Foreground;
        card.Padding = new Padding(14, 9, 14, 9);
        captureRoute = TextLabel("", 14, true);
        captureDistance = TextLabel("", 12, true);
        captureDistance.ForeColor = Accent;
        AddRows(card, TextLabel(L("ETS2 · EN CARRETERA", "ETS2 · ON THE ROAD"), 9, true), captureRoute, captureDistance);
        var border = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.Fuchsia };
        border.Controls.Add(card);
        window.Controls.Add(border);
        window.Resize += (_, _) => { if (captureRoute is not null) SetWrap(captureRoute, window.ClientSize.Width - 52); };
        captureWindow = window;
        window.FormClosed += (_, _) => { captureWindow = null; captureRoute = captureDistance = null; };
        ScaleText(window);
        SetWrap(captureRoute, window.ClientSize.Width - 52);
        RefreshPresence();
        window.Show(this);
    }

    private bool lowFuel;
    private int previousFuelThreshold;

    private void RenderTools(TelemetrySnapshot? trip, TelemetrySnapshot? live, bool discordConnected, bool sharing)
    {
        liveDelivery = live?.OnJob == true;
        var truck = trip?.Truck;
        var brand = truck?.Brand ?? "";
        var model = truck?.Model ?? "";
        if (!model.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) model = $"{brand} {model}".Trim();
        var vehicle = trip?.Coach == true ? L("Autobús", "Coach") : L("Camión", "Truck");
        truckName.Text = model.Length == 0 ? $"{vehicle} · {L("Sin datos", "No data")}" : model;
        vehicleMark.UpdateVehicle(trip?.Coach == true, shellPalette.Accent);
        var odo = trip?.Signals is { Odometer: var reading } && float.IsFinite(reading) && reading > 0
            ? AppOptions.Distance(reading, preferences.Features.Miles) : "—";
        var fuel = truck?.FuelPercent is { } percent ? $"{percent:N0}% · {truck.Litres:N0} L" : "—";
        fuelLevel.Visible = truck?.FuelPercent is not null;
        fuelLevel.Value = (int)Math.Clamp(truck?.FuelPercent ?? 0, 0, 100);
        fuelLevel.AccessibleName = L($"Combustible {fuel}", $"Fuel {fuel}");
        truckStats.Text = $"{L("Odómetro", "Odometer")}  {odo}   ·   {L("Combustible", "Fuel")}  {fuel}"
            + (truck?.RangeKm is > 0 and < 20_000 ? $"\n{L("Autonomía estimada", "Estimated fuel range")}  {AppOptions.Distance(truck.RangeKm.Value, preferences.Features.Miles)}" : "");
        var liveFuel = live?.Truck?.FuelPercent;
        var liveTruck = live?.Truck is { } current ? $"{current.Brand}|{current.Model}" : null;
        if (liveTruck is not null && liveTruck != fuelTruck) { fuelTruck = liveTruck; fuelAlerted = false; }
        var threshold = preferences.FuelThreshold;
        if (threshold != previousFuelThreshold || liveFuel is null) lowFuel = false;
        previousFuelThreshold = threshold;
        lowFuel = threshold > 0 && liveFuel is { } level && level < threshold + 3
            && (lowFuel || level <= threshold);
        var low = lowFuel;
        if (live is { Paused: false } && low && !fuelAlerted && AlertsAllowed(live))
        {
            fuelAlerted = true;
            tray.ShowBalloonTip(4500, L("Combustible bajo", "Low fuel"),
                L("Busca una estación en ETS2.", "Look for a fuel station in ETS2."), ToolTipIcon.Warning);
        }
        if (threshold <= 0 || liveFuel >= threshold + 3) fuelAlerted = false;
        fuelStatus.Visible = low;
        fuelStatus.Text = L("⚠  Combustible bajo · Busca una estación", "⚠  Low fuel · Look for a fuel station");

        var place = live is { OnJob: true } ? live.DestinationCity : live is null
            ? L("Sin viaje activo", "No active trip") : L("Conducción libre", "Free roaming");
        var remaining = live?.RemainingKm is { } km ? $"{AppOptions.Distance(km, preferences.Features.Miles)} {L("restantes", "remaining")}" : "—";
        if (quickText is not null)
            quickText.Text = $"{place}\n{remaining} · Discord {(discordConnected ? L("conectado", "connected") : L("sin conexión", "offline"))}"
                + (privateDestination ? $"\n{L("Destino oculto en Discord", "Destination hidden on Discord")}" : "");
        var visibility = PresencePrivacy.Apply(preferences, live?.OnJob, privateDestination);
        var idle = live?.Paused == true ? L("En pausa", "Paused") : L("Sin ruta activa", "No active route");
        var (obsRoute, obsDistance) = PresencePrivacy.Overlay(visibility, live, sharing, idle, remaining);
        if (captureRoute is not null) captureRoute.Text = preferences.Features.ObsRoute ? obsRoute : "";
        if (captureDistance is not null) captureDistance.Text = preferences.Features.ObsDistance ? obsDistance : "";
        overlay?.Update(obsRoute, obsDistance, preferences.Features);
    }

    private sealed class VehicleMark : Control
    {
        private bool coach;
        private Color ink;

        public VehicleMark()
        {
            Size = new Size(25, 23);
            Margin = new Padding(0, 4, 5, 0);
            TabStop = false;
        }

        public void UpdateVehicle(bool isCoach, Color color)
        {
            if (coach == isCoach && ink == color) return;
            coach = isCoach;
            ink = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.ScaleTransform(Width / 25f, Height / 23f);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var stroke = new Pen(ink, 1.5f);
            using var fill = new SolidBrush(ink);
            if (coach)
            {
                g.DrawRectangle(stroke, 2, 5, 21, 12);
                g.DrawLine(stroke, 3, 11, 22, 11);
                g.DrawLine(stroke, 15, 6, 15, 11);
            }
            else
            {
                g.DrawRectangle(stroke, 2, 5, 13, 12);
                g.DrawPolygon(stroke, [new Point(15, 8), new Point(20, 8), new Point(23, 12), new Point(23, 17), new Point(15, 17)]);
            }
            g.FillEllipse(fill, 5, 15, 5, 5);
            g.FillEllipse(fill, 17, 15, 5, 5);
        }
    }
}
