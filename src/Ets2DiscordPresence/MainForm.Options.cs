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
        var sidebarSize = Choice(style, L("Tamaño de la barra lateral", "Sidebar size"),
            [L("Automático", "Automatic"), L("Compacto", "Compact"), L("Completo", "Full")],
            current.SidebarSize switch { "compact" => 1, "full" => 2, _ => 0 });
        var reduceMotion = Check(style, L("Reducir movimiento", "Reduce motion"), current.ReduceMotion);
        int[] tableSizes = [100, 125, 150];
        var tableSize = Choice(style, L("Tamaño de texto en tablas", "Table text size"),
            tableSizes.Select(size => $"{size}%").ToArray(), Math.Max(0, Array.IndexOf(tableSizes, current.TableTextPercent)));
        var pins = new[] { "record", "privacy", "obs" };
        var pinChecks = pins.Select(key => Check(style, L("Fijar", "Pin") + " " + ActionName(key),
            current.Pinned?.Contains(key) == true)).ToArray();
        var alerts = Choice(game, L("Silenciar avisos", "Mute notifications"),
            [L("Nunca", "Never"), L("Durante entregas", "During deliveries"), L("Siempre", "Always")],
            current.AlertMode switch { "job" => 1, "always" => 2, _ => 0 });
        var rest = Check(game, L("Avisar cuando falte poco para descansar", "Warn when a rest stop is due"), current.RestNotice);
        var lowPower = Check(game, L("Reducir actualizaciones al minimizar", "Reduce updates when minimized"), current.LowPower);
        AddRows(game, TextLabel(L("Consulta el juego con menos frecuencia al minimizar, cuando estás fuera de una entrega.",
            "Checks the game less often when minimized and outside a delivery."), 9));
        var reconnect = Check(discord, L("Avisar cuando Discord se reconecte", "Notify on Discord reconnect"), current.ReconnectNotice);
        var gameMap = Check(discord, L("Detectar el mapa", "Detect the map"), current.RecognizeMap ?? current.RecognizeGameScreens);
        var gamePhoto = Check(discord, L("Detectar el modo foto", "Detect photo mode"), current.RecognizePhoto ?? current.RecognizeGameScreens);
        if (!GameScreenReader.HasRecognitionLanguage)
            AddRows(discord, TextLabel(L("Windows no tiene un idioma de reconocimiento de texto. Agrégalo en Configuración de Windows.",
                "Windows has no text recognition language installed. Add one in Windows Settings."), 9));
        var freeDrive = Check(game, L("Guardar conducción libre en el Diario", "Log free driving in the journal"), current.JournalFreeDrive);
        AddRows(discord, TextLabel(L("Lee los textos de la ventana de ETS2 en primer plano. Las imágenes se procesan en tu equipo.",
            "Reads text from the ETS2 window in the foreground. Images are processed on your device."), 9));
        var timerMode = Choice(discord, L("Duración que muestra Discord", "Discord timer duration"),
            [L("Actividad actual", "Current activity"), L("Sesión de juego", "Game session")], current.SessionTimer ? 1 : 0);
        var hideRoaming = Check(privacy, L("Ocultar ubicación en conducción libre", "Hide location in free roaming"), current.HideRoamingLocation);
        var hideDelivery = Check(privacy, L("Ocultar ubicación y ruta durante entregas", "Hide location and route during deliveries"), current.HideDeliveryLocation);
        AddRows(privacy, TextLabel(L("Estas reglas se aplican a Discord y a las fuentes de OBS.", "These rules apply to Discord and OBS sources."), 9));
        var pauseKey = Check(privacy, L("Atajo de pausa total · Ctrl + Alt + F11", "Pause sharing shortcut · Ctrl + Alt + F11"), current.PauseShortcut);
        var templates = AddTab(tabs, L("Plantillas", "Templates"));
        var roam = FeatureText(templates, L("Texto de conducción libre", "Free-roam headline"), current.RoamTemplate, 512);
        var delivery = FeatureText(templates, L("Texto de entrega", "Delivery headline"), current.DeliveryTemplate, 512);
        AddTemplatePreview(templates, roam, delivery);
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
        AddRows(obs, TextLabel(L("Activa la fuente para copiar su dirección y usarla en OBS.", "Enable the browser source to copy its address and use it in OBS."), 9));
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
        void BrowserOptionsChanged()
        {
            foreach (var control in new Control[] { waiting, position, font, opacity, radius }) control.Enabled = browser.Checked;
        }
        browser.CheckedChanged += (_, _) => BrowserOptionsChanged();
        BrowserOptionsChanged();
        var browserState = TextLabel("", 9);
        void LabelBrowserState()
        {
            browserState.Text = browser.Checked ? "" : L("Ajustes de fuente no disponibles · El navegador de OBS está desactivado.",
                "Source settings unavailable · The OBS browser source is off.");
            browserState.Visible = !browser.Checked;
        }
        browser.CheckedChanged += (_, _) => LabelBrowserState();
        LabelBrowserState();
        AddRows(obs, browserState);
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
        int[] keepCounts = [5, 10, 20];
        var backupKeep = Choice(data, L("Conservar copias automáticas", "Keep automatic backups"),
            keepCounts.Select(count => count.ToString()).ToArray(), Math.Max(0, Array.IndexOf(keepCounts, current.BackupKeep)));
        var backupWarning = Check(data, L("Avisar si todavía no hay copias", "Warn when no backup exists"), current.WarnMissingBackup);

        return () => preferences.Features with
        {
            Layout = layout.SelectedIndex switch { 1 => "drive", 2 => "stream", _ => "full" },
            SidebarSize = sidebarSize.SelectedIndex switch { 1 => "compact", 2 => "full", _ => "auto" },
            ReduceMotion = reduceMotion.Checked, TableTextPercent = tableSizes[tableSize.SelectedIndex],
            Miles = units.SelectedIndex == 1, Clock12 = hours.SelectedIndex == 1, HighContrast = contrast.Checked,
            AlertMode = alerts.SelectedIndex switch { 1 => "job", 2 => "always", _ => "normal" },
            Pinned = pins.Where((key, index) => pinChecks[index].Checked).ToArray(),
            RestNotice = rest.Checked, LowPower = lowPower.Checked, ReconnectNotice = reconnect.Checked,
            RecognizeMap = gameMap.Checked, RecognizePhoto = gamePhoto.Checked, JournalFreeDrive = freeDrive.Checked,
            SessionTimer = timerMode.SelectedIndex == 1, HideRoamingLocation = hideRoaming.Checked, HideDeliveryLocation = hideDelivery.Checked, PauseShortcut = pauseKey.Checked,
            RoamTemplate = roam.Text.Trim(), DeliveryTemplate = delivery.Text.Trim(), ObsWaiting = waiting.Text.Trim(),
            ObsFont = fontSizes[font.SelectedIndex], ObsOpacity = opacities[opacity.SelectedIndex], ObsRadius = radii[radius.SelectedIndex],
            ObsSize = sizes[obsSize.SelectedIndex], ObsRoute = obsRoute.Checked, ObsDistance = obsDistance.Checked,
            ObsBrowser = browser.Checked || !current.ObsBrowser && preferences.Features.ObsBrowser,
            ObsPosition = position.SelectedIndex switch { 1 => "center", 2 => "right", _ => "left" },
            AutoRecord = auto.Checked, RecordingLimitMb = limits[limit.SelectedIndex],
            BackupFolder = backup.Text.Trim(), BackupIntervalDays = intervals[interval.SelectedIndex],
            BackupKeep = keepCounts[backupKeep.SelectedIndex], WarnMissingBackup = backupWarning.Checked
        };
    }

    private static TextBox FeatureText(TableLayoutPanel panel, string caption, string? value, int maximum)
    {
        var field = new TextBox { Dock = DockStyle.Top, MaxLength = maximum, AccessibleName = caption, Text = value ?? "" };
        AddRows(panel, TextLabel(caption, 9, true), field);
        return field;
    }

    private void AddTemplatePreview(TableLayoutPanel panel, TextBox roam, TextBox delivery)
    {
        var scenario = Choice(panel, L("Ejemplo de Discord", "Discord sample"),
            [L("Entrega", "Delivery"), L("Conducción libre", "Free roam"), L("Pausa", "Paused")], 0);
        var previewLabel = TextLabel("", 10);
        AddRows(panel, previewLabel);
        void Update()
        {
            var example = scenario.SelectedIndex switch { 1 => Sample with { OnJob = false }, 2 => Sample with { Paused = true }, _ => Sample };
            var settings = preferences with { Options = preferences.Features with { RoamTemplate = roam.Text, DeliveryTemplate = delivery.Text } };
            var visibility = PresencePrivacy.Apply(settings, example.OnJob, privateDestination);
            var formatted = PresenceFormatter.Format(example, "Chambéry", visibility, "FR");
            var template = example.OnJob ? delivery.Text : roam.Text;
            var raw = example.Paused ? null : PresenceFormatter.Template(template, example, visibility,
                visibility.ShowCity ? Countries.Name("FR", English) : null);
            var bytes = System.Text.Encoding.UTF8.GetByteCount(raw ?? formatted.Details);
            var unknown = PresenceFormatter.UnknownVariables(roam.Text + " " + delivery.Text);
            var errors = unknown.Length > 0 ? L("Variables no reconocidas", "Unknown variables") + $" · {string.Join(", ", unknown)}\n" : "";
            previewLabel.Text = $"{errors}{bytes} / 128 bytes\n{formatted.Details}\n{formatted.State}";
            SetWrap(previewLabel, Math.Max(180, panel.Width - 40));
        }
        roam.TextChanged += (_, _) => Update();
        delivery.TextChanged += (_, _) => Update();
        scenario.SelectedIndexChanged += (_, _) => Update();
        panel.Resize += (_, _) => Update();
        Update();
    }

    private void AddSettingsSearch(Form owner, TabControl tabs)
    {
        var bar = Grid(1);
        bar.Padding = new Padding(8);
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = L("Buscar un ajuste…", "Find a setting…"),
            AccessibleName = L("Buscar un ajuste", "Find a setting") };
        var breadcrumb = TextLabel("", 9, true);
        void UpdateBreadcrumb()
        {
            var title = tabs.SelectedTab?.Text ?? "";
            breadcrumb.Text = title == L("Plantillas", "Templates")
                ? L("Ajustes › Discord › Plantillas", "Settings › Discord › Templates")
                : $"{L("Ajustes", "Settings")} › {title}";
        }
        tabs.SelectedIndexChanged += (_, _) => UpdateBreadcrumb();
        UpdateBreadcrumb();
        var results = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false,
            AccessibleName = L("Resultados de búsqueda", "Search results") };
        var input = Grid(3);
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var glass = TextLabel("⌕", 14, true);
        var clear = MakeButton("×");
        clear.AccessibleName = L("Borrar búsqueda", "Clear search");
        clear.Enabled = false;
        clear.Click += (_, _) => { search.Clear(); search.Focus(); };
        input.Controls.Add(glass, 0, 0);
        input.Controls.Add(search, 1, 0);
        input.Controls.Add(clear, 2, 0);
        AddRows(bar, breadcrumb, input, results);
        owner.Controls.Add(bar);
        var highlight = new System.Windows.Forms.Timer { Interval = 1400 };
        Control? highlighted = null;
        Color original = Color.Empty;
        void ResetHighlight()
        {
            highlight.Stop();
            if (highlighted is not null && !highlighted.IsDisposed) highlighted.BackColor = original;
            highlighted = null;
        }
        highlight.Tick += (_, _) => ResetHighlight();
        owner.Disposed += (_, _) => highlight.Dispose();
        search.TextChanged += (_, _) =>
        {
            results.Items.Clear();
            var query = search.Text.Trim();
            clear.Enabled = search.TextLength > 0;
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
            ResetHighlight();
            highlighted = match.Control;
            original = highlighted.BackColor;
            highlighted.BackColor = shellPalette.Hover;
            highlight.Start();
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
        public override string ToString() => $"{Title}   —   {Page.Text}";
    }
}
