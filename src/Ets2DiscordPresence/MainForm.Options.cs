namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private Func<AppOptions> BuildFeatureSettings(Form owner, TabControl tabs, TableLayoutPanel style,
        TableLayoutPanel discord, TableLayoutPanel privacy, TableLayoutPanel game, TableLayoutPanel data)
    {
        var current = preferences.Features;
        var layout = Choice(style, L("Perfil de interfaz", "Dashboard profile"),
            [L("Completo", "Full"), L("Conducción", "Driving"), L("Transmisión", "Streaming")],
            current.Layout switch { "drive" => 1, "stream" => 2, _ => 0 });
        var units = Choice(style, L("Unidad de distancia", "Distance unit"), ["km", "mi"], current.Miles ? 1 : 0);
        var hours = Choice(style, L("Formato de hora", "Time format"), ["24 h", "12 h"], current.Clock12 ? 1 : 0);
        var contrast = Check(style, L("Texto e indicadores de alto contraste", "High-contrast text and indicators"), current.HighContrast);
        var pins = new[] { "record", "privacy", "obs" };
        var pinChecks = pins.Select(key => Check(style, L("Fijar", "Pin") + " " + ActionName(key),
            current.Pinned?.Contains(key) == true)).ToArray();
        var alerts = Choice(game, L("Silenciar avisos", "Mute notifications"),
            [L("Nunca", "Never"), L("Durante entregas", "During deliveries"), L("Siempre", "Always")],
            current.AlertMode switch { "job" => 1, "always" => 2, _ => 0 });
        var rest = Check(game, L("Avisar cuando falte poco para descansar", "Warn when a rest stop is due"), current.RestNotice);
        var lowPower = Check(game, L("Reducir actualizaciones al minimizar", "Reduce updates when minimized"), current.LowPower);
        var reconnect = Check(discord, L("Avisar cuando Discord se reconecte", "Notify on Discord reconnect"), current.ReconnectNotice);
        var timerMode = Choice(discord, L("Duración que muestra Discord", "Discord timer duration"),
            [L("Actividad actual", "Current activity"), L("Sesión de juego", "Game session")], current.SessionTimer ? 1 : 0);
        var hideRoaming = Check(privacy, L("Ocultar ubicación en conducción libre", "Hide location in free roaming"), current.HideRoamingLocation);
        var hideDelivery = Check(privacy, L("Ocultar ubicación y ruta durante entregas", "Hide location and route during deliveries"), current.HideDeliveryLocation);
        var pauseKey = Check(privacy, L("Atajo de pausa total · Ctrl + Alt + F11", "Pause sharing shortcut · Ctrl + Alt + F11"), current.PauseShortcut);
        var templates = AddTab(tabs, L("Plantillas", "Templates"));
        var roam = FeatureText(templates, L("Texto de conducción libre", "Free-roam headline"), current.RoamTemplate, 128);
        var delivery = FeatureText(templates, L("Texto de entrega", "Delivery headline"), current.DeliveryTemplate, 128);
        var presets = Choice(templates, L("Cargar una plantilla", "Load a template"),
            [L("Elegir…", "Choose…"), L("Solo actividad", "Activity only"), L("Camión y carga", "Truck and cargo"),
                L("Exploración", "Exploration")], 0);
        presets.SelectionChangeCommitted += (_, _) =>
        {
            switch (presets.SelectedIndex)
            {
                case 1: roam.Text = ""; delivery.Text = ""; break;
                case 2:
                    roam.Text = L("Conduciendo un {camión}", "Driving a {truck}");
                    delivery.Text = L("{camión} · {carga}", "{truck} · {cargo}");
                    break;
                case 3:
                    roam.Text = L("De paseo por {país}", "Exploring {country}");
                    delivery.Text = "";
                    break;
            }
            presets.SelectedIndex = 0;
        };
        AddRows(templates, TextLabel(L("Deja el texto vacío para usar la plantilla normal. Variables: {país}, {camión}, {carga}, {destino}.",
            "Leave blank for the default. Variables: {country}, {truck}, {cargo}, {destination}."), 9));
        var obs = AddTab(tabs, "OBS");
        var waiting = FeatureText(obs, L("Mensaje de espera en OBS", "OBS waiting message"), current.ObsWaiting, 120);
        var sizes = new[] { 75, 100, 125, 150 };
        var obsSize = Choice(obs, L("Tamaño de la tarjeta", "Card size"), sizes.Select(size => $"{size}%").ToArray(),
            Math.Max(0, Array.IndexOf(sizes, current.ObsSize)));
        var obsDistance = Check(obs, L("Mostrar distancia en OBS", "Show distance in OBS"), current.ObsDistance);
        var browser = Check(obs, L("Activar la fuente de navegador local", "Enable local browser source"), current.ObsBrowser);
        var obsRoute = Check(obs, L("Mostrar ruta en OBS", "Show route in OBS"), current.ObsRoute);
        var position = Choice(obs, L("Posición en la fuente de navegador", "Browser source position"),
            [L("Izquierda", "Left"), L("Centro", "Center"), L("Derecha", "Right")],
            current.ObsPosition switch { "center" => 1, "right" => 2, _ => 0 });
        var fontSizes = new[] { 16, 22, 28, 36 };
        var font = Choice(obs, L("Tamaño de letra", "Font size"), fontSizes.Select(value => $"{value} px").ToArray(),
            Math.Max(0, Array.IndexOf(fontSizes, current.ObsFont)));
        var opacities = new[] { 0, 50, 75, 93, 100 };
        var opacity = Choice(obs, L("Opacidad del fondo", "Background opacity"), opacities.Select(value => $"{value} %").ToArray(),
            Math.Max(0, Array.IndexOf(opacities, current.ObsOpacity)));
        var radii = new[] { 0, 8, 16, 24 };
        var radius = Choice(obs, L("Redondeo de bordes", "Corner rounding"), radii.Select(value => $"{value} px").ToArray(),
            Math.Max(0, Array.IndexOf(radii, current.ObsRadius)));
        BrowserOverlay? previewServer = null;
        var previewTimer = new System.Windows.Forms.Timer { Interval = 500 };
        owner.FormClosed += (_, _) =>
        {
            previewTimer.Dispose();
            previewServer?.Dispose();
        };
        AppOptions PreviewOptions() => preferences.Features with
        {
            ObsRoute = obsRoute.Checked, ObsDistance = obsDistance.Checked, ObsWaiting = waiting.Text.Trim(),
            ObsSize = sizes[obsSize.SelectedIndex], ObsFont = fontSizes[font.SelectedIndex],
            ObsOpacity = opacities[opacity.SelectedIndex], ObsRadius = radii[radius.SelectedIndex],
            ObsPosition = position.SelectedIndex switch { 1 => "center", 2 => "right", _ => "left" }
        };
        void RefreshPreview()
        {
            if (previewServer is null) return;
            var options = PreviewOptions();
            var sharing = preferences.Sharing && !privateForSession && !(privateUntil > DateTimeOffset.UtcNow)
                && !demo && demoScenario is null && !replayOpen;
            var live = liveSnapshot;
            var visibility = PresencePrivacy.Apply(preferences with { Options = options }, live?.OnJob, privateDestination);
            var distance = live?.RemainingKm is { } km ? AppOptions.Distance(km, options.Miles) : "";
            var idle = live?.Paused == true ? L("En pausa", "Paused") : L("Sin ruta activa", "No active route");
            var (routeText, distanceText) = PresencePrivacy.Overlay(visibility, live, sharing, idle, distance);
            previewServer.Update(routeText, distanceText, options);
        }
        previewTimer.Tick += (_, _) => RefreshPreview();
        AddAction(obs, L("Vista previa de la fuente en el navegador", "Preview the source in browser"), () =>
        {
            try
            {
                previewServer ??= new BrowserOverlay(Guid.NewGuid().ToString("N"));
                RefreshPreview();
                previewTimer.Start();
                OpenExternal(previewServer.Url);
            }
            catch (System.Net.Sockets.SocketException)
            {
                MessageBox.Show(owner, L("No hay un puerto disponible para la vista previa.", "No port is available for the preview."), Text);
            }
        });
        AddAction(obs, L("Regenerar dirección OBS", "Regenerate OBS URL"), () => RegenerateObsUrl(owner));
        AddAction(obs, L("Abrir ventana OBS", "Open OBS window"), ShowCaptureCard);
        AddAction(obs, L("Copiar dirección de la fuente OBS", "Copy OBS browser source URL"), () => CopyObsUrl(owner));
        var auto = Check(data, L("Grabar automáticamente cada entrega", "Automatically record each delivery"), current.AutoRecord);
        var limits = new[] { 0, 128, 256, 512, 1024, 2048 };
        var limit = Choice(data, L("Espacio máximo para grabaciones", "Maximum space for recordings"),
            limits.Select(mb => mb == 0 ? L("Sin límite", "Unlimited") : $"{mb} MB").ToArray(),
            Math.Max(0, Array.IndexOf(limits, current.RecordingLimitMb)));
        var backup = FeatureText(data, L("Carpeta de copias automáticas", "Automatic backup folder"), current.BackupFolder, 450);
        AddAction(data, L("Elegir carpeta de copias…", "Choose backup folder…"), () =>
        {
            using var picker = new FolderBrowserDialog();
            if (picker.ShowDialog(owner) == DialogResult.OK) backup.Text = picker.SelectedPath;
        });
        var intervals = new[] { 1, 7, 30 };
        var interval = Choice(data, L("Frecuencia de copias mientras la app está abierta", "Backup frequency while the app is open"),
            intervals.Select(days => L($"{days} día(s)", $"{days} day(s)")).ToArray(), Math.Max(0, Array.IndexOf(intervals, current.BackupIntervalDays)));

        return () => preferences.Features with
        {
            Layout = layout.SelectedIndex switch { 1 => "drive", 2 => "stream", _ => "full" },
            Miles = units.SelectedIndex == 1, Clock12 = hours.SelectedIndex == 1, HighContrast = contrast.Checked,
            AlertMode = alerts.SelectedIndex switch { 1 => "job", 2 => "always", _ => "normal" },
            Pinned = pins.Where((key, index) => pinChecks[index].Checked).ToArray(),
            RestNotice = rest.Checked, LowPower = lowPower.Checked, ReconnectNotice = reconnect.Checked,
            SessionTimer = timerMode.SelectedIndex == 1, HideRoamingLocation = hideRoaming.Checked, HideDeliveryLocation = hideDelivery.Checked, PauseShortcut = pauseKey.Checked,
            RoamTemplate = roam.Text.Trim(), DeliveryTemplate = delivery.Text.Trim(), ObsWaiting = waiting.Text.Trim(),
            ObsFont = fontSizes[font.SelectedIndex], ObsOpacity = opacities[opacity.SelectedIndex], ObsRadius = radii[radius.SelectedIndex],
            ObsSize = sizes[obsSize.SelectedIndex], ObsRoute = obsRoute.Checked, ObsDistance = obsDistance.Checked,
            ObsBrowser = browser.Checked || !current.ObsBrowser && preferences.Features.ObsBrowser,
            ObsPosition = position.SelectedIndex switch { 1 => "center", 2 => "right", _ => "left" },
            AutoRecord = auto.Checked, RecordingLimitMb = limits[limit.SelectedIndex],
            BackupFolder = backup.Text.Trim(), BackupIntervalDays = intervals[interval.SelectedIndex]
        };
    }

    private static TextBox FeatureText(TableLayoutPanel panel, string caption, string? value, int maximum)
    {
        var field = new TextBox { Dock = DockStyle.Top, MaxLength = maximum, AccessibleName = caption, Text = value ?? "" };
        AddRows(panel, TextLabel(caption, 9, true), field);
        return field;
    }

    private void AddSettingsSearch(Form owner, TabControl tabs)
    {
        var bar = Grid(1);
        bar.Padding = new Padding(8);
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = L("Buscar un ajuste…", "Find a setting…") };
        var results = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false,
            AccessibleName = L("Resultados de búsqueda", "Search results") };
        AddRows(bar, search, results);
        owner.Controls.Add(bar);
        search.TextChanged += (_, _) =>
        {
            results.Items.Clear();
            var query = search.Text.Trim();
            results.Visible = query.Length > 0;
            if (query.Length == 0) return;
            foreach (TabPage page in tabs.TabPages)
            foreach (var target in SettingsControls(page))
            {
                var text = target.AccessibleName ?? target.Text;
                if (!string.IsNullOrWhiteSpace(text) && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(text,
                    query, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0)
                    results.Items.Add(new SettingsMatch(page, target, text));
            }
            if (results.Items.Count == 0) results.Items.Add(L("Sin resultados", "No results"));
            results.SelectedIndex = 0;
        };
        void OpenMatch()
        {
            if (results.SelectedItem is not SettingsMatch match) return;
            tabs.SelectedTab = match.Page;
            match.Page.ScrollControlIntoView(match.Control);
            match.Control.Focus();
        }
        results.SelectionChangeCommitted += (_, _) => OpenMatch();
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || search.TextLength == 0) return;
            OpenMatch();
            e.SuppressKeyPress = true;
        };
    }

    private static IEnumerable<Control> SettingsControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button or CheckBox or ComboBox or TextBox or CheckedListBox) yield return child;
            foreach (var inner in SettingsControls(child)) yield return inner;
        }
    }

    private sealed record SettingsMatch(TabPage Page, Control Control, string Title)
    {
        public override string ToString() => $"{Page.Text} · {Title}";
    }
}
