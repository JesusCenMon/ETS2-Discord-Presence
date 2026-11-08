using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private const int PrivacyHotkeyId = 81;
    private bool privateDestination;
    private bool hotkeyRegistered;
    private bool fuelAlerted;
    private bool liveDelivery;
    private bool retentionChecked;
    private string? fuelTruck;
    private string? readerError;
    private readonly Label truckName = TextLabel("", 12, true);
    private readonly Label truckStats = TextLabel("", 10);
    private readonly Label fuelStatus = TextLabel("", 10, true);
    private readonly ToolStripMenuItem trayQuick = new();
    private Form? quickWindow;
    private Form? captureWindow;
    private Label? quickText;
    private Label? captureRoute;
    private Label? captureDistance;
    private readonly ConditionalWeakTable<Control, TextSize> textSizes = new();

    private sealed class TextSize(Font baseline)
    {
        public Font Baseline { get; } = baseline;
        public Font? Applied { get; set; }
    }

    private Form ToolWindow(string title, Size size) => new()
    {
        Text = title, ClientSize = size, MinimumSize = new Size(Math.Min(size.Width, 520), 360),
        StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Foreground,
        AutoScaleMode = AutoScaleMode.Dpi, Icon = appIcon
    };

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
            var percent = Math.Clamp(preferences.TextPercent, 85, 135);
            var original = control == cargo && fullScreen ? 34 : control == distance && fullScreen ? 42 : baseline.Baseline.SizeInPoints;
            var target = original * percent / 100;
            if (Math.Abs(control.Font.SizeInPoints - target) > .02f)
            {
                var replacement = new Font(baseline.Baseline.FontFamily, target, baseline.Baseline.Style);
                control.Font = replacement;
                baseline.Applied?.Dispose();
                baseline.Applied = replacement;
            }
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
        bar.Controls.AddRange([smaller, larger, center]);
        return bar;
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
        RefreshPresence();
    }

    private void RegisterPrivacyShortcut()
    {
        if (!IsHandleCreated) return;
        if (hotkeyRegistered) UnregisterHotKey(Handle, PrivacyHotkeyId);
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
        hotkeyRegistered = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
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
        label.MaximumSize = new Size(350, 0);
        dashboard.Click += (_, _) => { window.Close(); RestoreWindow(); };
        privacy.Click += (_, _) => ToggleDestinationPrivacy();
        AddRows(root, TextLabel(L("VIAJE ACTUAL", "CURRENT TRIP"), 10, true), label, bar);
        window.Controls.Add(root);
        window.Deactivate += (_, _) => window.Close();
        window.FormClosed += (_, _) => { quickText = null; quickWindow = null; };
        StyleTool(window);
        RefreshPresence();
        window.Show();
    }

    private void ShowCaptureCard()
    {
        if (captureWindow is { IsDisposed: false }) { captureWindow.Activate(); return; }
        var window = ToolWindow("ETS2 · OBS", new Size(560, 155));
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

    private void RenderTools(TelemetrySnapshot? trip, TelemetrySnapshot? live, bool discordConnected)
    {
        liveDelivery = live?.OnJob == true;
        var truck = trip?.Truck;
        var brand = truck?.Brand ?? "";
        var model = truck?.Model ?? "";
        if (!model.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) model = $"{brand} {model}".Trim();
        truckName.Text = model.Length == 0 ? L("Camión · Sin datos", "Truck · No data") : model;
        var odo = trip?.Signals is { Odometer: var reading } && float.IsFinite(reading) && reading > 0
            ? $"{reading:N0} km" : "—";
        var fuel = truck?.FuelPercent is { } percent ? $"{percent:N0}% · {truck.Litres:N0} L" : "—";
        truckStats.Text = $"{L("Odómetro", "Odometer")}  {odo}   ·   {L("Combustible", "Fuel")}  {fuel}";
        var liveFuel = live?.Truck?.FuelPercent;
        var liveTruck = live?.Truck is { } current ? $"{current.Brand}|{current.Model}" : null;
        if (liveTruck is not null && liveTruck != fuelTruck) { fuelTruck = liveTruck; fuelAlerted = false; }
        var threshold = preferences.FuelThreshold;
        var low = threshold > 0 && liveFuel <= threshold;
        if (live is { Paused: false } && low && !fuelAlerted)
        {
            fuelAlerted = true;
            tray.ShowBalloonTip(4500, L("Combustible bajo", "Low fuel"),
                L("Busca una estación en ETS2.", "Look for a fuel station in ETS2."), ToolTipIcon.Warning);
        }
        if (threshold <= 0 || liveFuel >= threshold + 3) fuelAlerted = false;
        fuelStatus.Visible = low;
        fuelStatus.Text = L("Combustible bajo · Busca una estación", "Low fuel · Look for a fuel station");

        var place = live is { OnJob: true } ? live.DestinationCity : live is null
            ? L("Sin viaje activo", "No active trip") : L("Conducción libre", "Free roaming");
        var remaining = live?.RemainingKm is { } km ? $"{km:N0} km {L("restantes", "remaining")}" : "—";
        if (quickText is not null)
            quickText.Text = $"{place}\n{remaining} · Discord {(discordConnected ? L("conectado", "connected") : L("sin conexión", "offline"))}"
                + (privateDestination ? $"\n{L("Destino oculto en Discord", "Destination hidden on Discord")}" : "");
        if (captureRoute is not null)
            captureRoute.Text = live is { OnJob: true } ? $"{live.SourceCity}  →  {live.DestinationCity}" : place;
        if (captureDistance is not null) captureDistance.Text = remaining;
    }
}
