namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private readonly Dictionary<Control, (string Es, string En)> translations = [];
    private readonly Label timeline = TextLabel("", 10, true);
    private readonly Label deadlineAlert = TextLabel("", 10, true);
    private readonly Label onboarding = TextLabel("", 10);
    private readonly Label finishBanner = TextLabel("", 11, true);
    private readonly Button historyButton = MakeButton("Historial");
    private readonly Button summaryButton = MakeButton("Ver resumen");
    private readonly TripMap tripMap = new();
    private readonly string[] panelKeys = ["trip", "discord", "map"];
    private readonly Dictionary<string, Control> panelSections = [];
    private FormWindowState previousWindowState;
    private Rectangle previousBounds;
    private bool fullScreen;

    private bool English => preferences.Language == "en";
    private string L(string es, string en) => English ? en : es;

    private T Translate<T>(T control, string es, string en) where T : Control
    {
        translations[control] = (es, en);
        control.Text = L(es, en);
        return control;
    }

    private Label UiLabel(string es, string en, int size, bool bold = false) =>
        Translate(TextLabel(es, size, bold), es, en);

    private void ApplyLanguage()
    {
        foreach (var (control, pair) in translations)
            control.Text = L(pair.Es, pair.En);
        trayMenu.Items[0].Text = L("Abrir panel", "Open dashboard");
        trayQuick.Text = L("Ver viaje rápido", "View quick trip");
        trayMenu.Items[3].Text = L("Licencias", "Licenses");
        trayMenu.Items[4].Text = L("Salir", "Exit");
        UpdateSharingControls();
    }

    private static void Restyle(Control control)
    {
        switch (control)
        {
            case Panel or Label:
                control.TabStop = false;
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                break;
            case CheckBox check:
                check.FlatStyle = FlatStyle.Flat;
                check.CheckAlign = check.TextAlign = ContentAlignment.MiddleLeft;
                break;
            case RadioButton radio:
                radio.FlatStyle = FlatStyle.Flat;
                radio.CheckAlign = radio.TextAlign = ContentAlignment.MiddleLeft;
                break;
        }
        var children = control.Controls.Cast<Control>();
        if (control is TableLayoutPanel table)
            children = children.OrderBy(table.GetRow).ThenBy(table.GetColumn);
        var index = 0;
        foreach (var child in children)
        {
            child.TabIndex = index++;
            Restyle(child);
        }
    }

    private string PanelName(string key) => key switch
    {
        "trip" => L("Viaje activo", "Active trip"),
        "discord" => L("Vista previa de Discord", "Discord preview"),
        "map" => L("Mapa", "Map"),
        _ => L("Ilustración", "Artwork")
    };

    private string FieldName(string key) => key switch
    {
        "cargo" => L("Carga", "Cargo"), "route" => L("Ruta", "Route"),
        "city" => L("Ciudad", "City"), _ => L("Distancia", "Distance")
    };

    private static string[] OrderPanels(AppSettings settings, string[] keys) =>
        (settings.PanelOrder ?? keys).Where(keys.Contains).Concat(keys).Distinct().ToArray();

    private void ConfigurePanels()
    {
        foreach (var (key, control) in panelSections)
            control.Visible = preferences.HiddenPanels?.Contains(key) != true
                && (!preferences.Compact || fullScreen || key == "trip") && (!fullScreen || key is "trip" or "map");

        OrderTripPanels();
        ArrangeLayout();
    }

    private void OrderTripPanels()
    {
        var order = OrderPanels(preferences, panelKeys);
        var rows = new[] { 0, 4, 5 };
        tripPanel.SuspendLayout();
        for (var index = 0; index < order.Length; index++)
        {
            if (panelSections[order[index]].Parent == tripPanel)
                tripPanel.SetRow(panelSections[order[index]], rows[index]);
        }
        tripPanel.ResumeLayout(true);
        Restyle(tripPanel);
    }

    private void RenderExtras(TelemetrySnapshot? trip, long now, bool running, bool ready)
    {
        onboarding.Visible = !preferences.GuideDismissed && !fullScreen && demoScenario is null;
        onboarding.Text = $"{L("Primera conexión", "First connection")}\n"
            + $"1  {(gamePrepared ? "✓" : "○")}  {L("Lector del juego", "Game reader")}   "
            + $"2  {(ready ? "✓" : "○")}  Discord   "
            + $"3  {(liveSnapshot is not null ? "✓" : "○")}  {L("Partida", "Game session")}\n"
            + (restartNeeded ? L("Reinicia ETS2 para cargar el lector.", "Restart ETS2 to load the reader.")
                : !gamePrepared ? L("Revisa la preparación del lector en Ajustes.", "Check reader setup in Settings.")
                : !running ? L("Abre ETS2 para seguir.", "Launch ETS2 to continue.")
                : !ready ? L("Abre Discord de escritorio.", "Open Discord desktop.")
                : L("Entra al camión para enviar la actividad.", "Enter your truck to send activity."));

        var stage = trip?.OnJob == true
            ? trip.Signals is { Loaded: false } && !demo ? 0
                : trip.RemainingKm is < .3 && trip.Signals is { ParkBrake: true } ? 3
                : trip.RemainingKm is < 20 ? 2 : 1
            : -1;
        var phases = new[] { L("Recogida", "Pickup"), L("En ruta", "On the road"), L("Llegando", "Arriving"), L("Descarga probable", "Likely unloading") };
        var steps = string.Join("   ·   ", phases.Select((phase, index) => index == stage ? $"● {phase}" : phase));
        var manoeuvre = trip is not null && !demo ? journal.Manoeuvre(trip, now) switch
        {
            "ferry" => L("Ferry detectado", "Ferry detected"),
            "rest" => L("Descanso probable", "Likely rest stop"),
            "fuel" => L("Repostando", "Refueling"),
            "parked" => L("Estacionado", "Parked"),
            "reversing" => L("Marcha atrás", "Reversing"),
            _ => null
        } : null;
        timeline.Visible = (stage >= 0 || manoeuvre is not null) && preferences.HiddenPanels?.Contains("timeline") != true;
        timeline.Text = string.Join(Environment.NewLine, new[] { stage >= 0 ? steps : null, manoeuvre,
            journal.Recovered && stage >= 0 && !demo ? L("Viaje recuperado del diario", "Trip resumed from the journal") : null }.OfType<string>());
        summaryButton.Visible = finishBanner.Visible = completedTrip is not null && !demo && demoScenario is null && !replayOpen;
        if (completedTrip is { } completed)
            finishBanner.Text = $"{Result(completed)} · {completed.Origin} → {completed.Destination} · {AppOptions.Distance(completed.Km, preferences.Features.Miles, "N1")} · {FormatDriving(completed.DrivingSeconds)}";
        deadlineAlert.Visible = false;
        if (trip is { OnJob: true, Paused: false, RouteSeconds: > 0 and < 604800, Signals.Deadline: > 0 }
            && trip.RemainingKm.HasValue)
        {
            var margin = (long)trip.Signals.Deadline - trip.GameMinutes - (long)Math.Ceiling(trip.RouteSeconds / 60d);
            if (margin <= 30)
            {
                deadlineAlert.Visible = true;
                deadlineAlert.ForeColor = Foreground;
                deadlineAlert.Text = margin < 0 ? L("La llegada estimada supera el plazo.", "Estimated arrival exceeds the deadline.")
                    : L($"Margen antes del plazo: {margin} min del juego.", $"Time before deadline: {margin} in-game min.");
            }
        }
    }

    private void ToggleMonitor()
    {
        if (fullScreen)
        {
            fullScreen = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            Bounds = previousBounds;
            WindowState = previousWindowState;
        }
        else
        {
            previousWindowState = WindowState;
            previousBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var current = Screen.FromControl(this);
            var target = Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName != current.DeviceName) ?? current;
            fullScreen = true;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = target.Bounds;
        }
        ScaleText(this);
        ConfigurePanels();
        RefreshPresence();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (fullScreen && keyData == Keys.Escape)
        {
            ToggleMonitor();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void LoadCatalog()
    {
        if (preferences.CityCatalog is { Length: > 0 } name && Path.GetFileName(name) == name)
        {
            try
            {
                using var data = File.OpenRead(Path.Combine(TripJournal.DataFolder, "catalogs", name));
                if (data.Length > 5_000_000) throw new InvalidDataException();
                cities = new CityLookup(data);
                return;
            }
            catch (Exception ex) when (IsCatalogError(ex))
            {
                settingsError = L("El catálogo elegido no se pudo abrir. Se está usando el incluido.", "The selected catalog could not be loaded. The built-in catalog is active.");
            }
        }
        using var bundled = AppResources.Open("cities_default.json");
        cities = new CityLookup(bundled);
    }

    private static bool IsCatalogError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException;

    private void MaintainReader(bool remove)
    {
        if (IsGameRunning())
        {
            MessageBox.Show(this, L("Cierra ETS2 antes de cambiar el lector.", "Close ETS2 before changing the reader."), Text);
            return;
        }
        if (gamePath is null)
        {
            MessageBox.Show(this, L("Localiza ETS2 primero.", "Locate ETS2 first."), Text);
            return;
        }
        if (remove && MessageBox.Show(this, L("¿Retirar el lector instalado por esta app?", "Remove the reader installed by this app?"),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            PluginInstaller.Maintain(gamePath, remove);
            SavePreferences(preferences with { ReaderDisabled = remove });
            PrepareGame(gamePath);
            RefreshPresence();
            MessageBox.Show(this, remove ? L("Lector retirado.", "Reader removed.") : L("Lector reparado.", "Reader repaired."), Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, L("No se pudo cambiar el lector.", "The reader could not be changed.") + "\n" + ex.Message, Text);
        }
    }
}
