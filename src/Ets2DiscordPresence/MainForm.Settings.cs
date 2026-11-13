namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ShowSettings(string initialTab = "")
    {
        using var window = ToolWindow(L("Ajustes", "Settings"), new Size(740, 560), placement: "settings");
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var apply = MakeButton(L("Guardar cambios", "Save changes"));
        var cancel = MakeButton(L("Cancelar", "Cancel"));
        var restart = MakeButton(L("Reiniciar aplicación ahora", "Restart application now"));
        restart.Visible = preferences.Theme != initialTheme || portableRestartPending;
        restart.Click += (_, _) => RestartApplication(window);
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange([apply, cancel, restart]);
        window.Controls.Add(tabs);
        window.Controls.Add(footer);
        window.AcceptButton = apply;
        window.CancelButton = cancel;

        var discordTab = AddTab(tabs, "Discord");
        var privacy = AddTab(tabs, L("Privacidad", "Privacy"));
        AddRows(discordTab, TextLabel(L("Qué se muestra", "What appears"), 14, true));
        AddRows(privacy, TextLabel(L("Privacidad", "Privacy"), 14, true));
        var game = AddTab(tabs, L("Juego", "Game"));
        var style = AddTab(tabs, L("Apariencia", "Appearance"));
        var updateChannel = Choice(game, L("Canal de actualizaciones", "Update channel"),
            ["Stable", "Preview"], preferences.PreviewUpdates ? 1 : 0);
        AddRows(game, TextLabel(L("Stable recibe versiones finales. Preview también recibe versiones de prueba.", "Stable receives final releases. Preview also receives test releases."), 9));
        var data = AddTab(tabs, L("Datos locales", "Local data"));
        var lastBackup = LastBackup()?.LocalDateTime.ToString("g") ?? L("Nunca", "Never");
        AddRows(data, TextLabel(L($"Última copia verificada · {lastBackup}", $"Last verified backup · {lastBackup}"), 10));
        if (preferences.Features.WarnMissingBackup && LastBackup() is null && journal.Entries.Count >= 20)
            AddRows(data, TextLabel(L("Ya tienes 20 viajes o más. Guarda una copia del diario.", "You have 20 or more trips. Save a backup of your journal."), 10, true));
        AddAction(data, L("Ver almacenamiento…", "View storage…"), () => ShowStorage(window));
        AddAction(data, L("Inspeccionar copia…", "Inspect backup…"), () => InspectBackup(window));
        int[] textSizes = [85, 100, 115, 135, 150];
        var getFeatures = BuildFeatureSettings(window, tabs, style, discordTab, privacy, game, data);
        AddSettingsSearch(window, tabs);
        var textSize = Choice(style, L("Tamaño del texto", "Text size"), textSizes.Select(value => $"{value}%").ToArray(),
            Math.Max(0, Array.IndexOf(textSizes, preferences.TextPercent)));
        var shortcuts = Choice(privacy, L("Atajo global para ocultar el destino", "Global shortcut to hide the destination"),
            [L("Desactivado", "Off"), "Ctrl + Alt + F8", "Ctrl + Alt + F9", "Ctrl + Alt + F10"], Math.Clamp(preferences.PrivacyShortcut, 0, 3));
        var shortcutKeys = ToolBar();
        var keyLabels = new[] { "Ctrl", "Alt", "F8" }.Select(key =>
        {
            var badge = TextLabel($"  {key}  ", 9, true);
            badge.BorderStyle = BorderStyle.FixedSingle;
            return badge;
        }).ToArray();
        shortcutKeys.Controls.AddRange(keyLabels);
        void ShowKeys()
        {
            shortcutKeys.Visible = shortcuts.SelectedIndex > 0;
            keyLabels[2].Text = $"  F{7 + shortcuts.SelectedIndex}  ";
        }
        shortcuts.SelectedIndexChanged += (_, _) => ShowKeys();
        ShowKeys();
        AddRows(privacy, shortcutKeys);
        AddRows(discordTab, TextLabel(L("Portadas", "Artwork"), 14, true));
        var image = Check(discordTab, L("Mostrar imagen del juego", "Show game artwork"), preferences.ActivityImage);
        var imageState = TextLabel("", 9, true);
        AddRows(discordTab, imageState);
        AddRows(discordTab, TextLabel(L("Usa la portada del país cuando compartes la ubicación.", "Uses country artwork when you share your location."), 9));
        var country = Check(discordTab, L("Mostrar país y bandera", "Show country and flag"), preferences.SharesCountry);
        var activityTimer = Check(discordTab, L("Mostrar tiempo transcurrido", "Show elapsed time"), preferences.ActivityTimer);
        int[] thresholds = [0, 10, 15, 20, 25, 30, 40];
        var fuel = Choice(game, L("Aviso de combustible al llegar a", "Fuel alert at"),
            thresholds.Select(value => value == 0 ? L("Desactivado", "Off") : $"{value}%").ToArray(),
            Math.Max(0, Array.IndexOf(thresholds, preferences.FuelThreshold)));
        int[] retentionValues = [0, 7, 30, 90];
        var retention = Choice(data, L("Conservar grabaciones locales", "Keep local recordings"),
            retentionValues.Select(value => value == 0 ? L("Siempre", "Forever") : L($"{value} días", $"{value} days")).ToArray(),
            Math.Max(0, Array.IndexOf(retentionValues, preferences.RetentionDays)));
        AddRows(data, TextLabel(LocalData.IsPortable ? L("Datos junto al ejecutable", "Data beside the executable")
            : L("Datos en el perfil de Windows", "Data in the Windows profile"), 10, true));
        AddRows(data, TextLabel(L("Modo portátil · Requiere reiniciar", "Portable mode · Requires restart"), 9));
        AddAction(data, LocalData.IsPortable ? L("Volver a guardar en Windows…", "Use the Windows profile…")
            : L("Activar modo portátil…", "Enable portable mode…"), () => ChangeStorage(window));
        AddAction(data, L("Exportar copia de seguridad…", "Export backup…"), () => ExportBackup(window));
        AddAction(data, L("Restaurar copia de seguridad…", "Restore backup…"), () => RestoreBackup(window));
        AddAction(data, L("¿Qué hace esto? · Portátil y restauración", "What does this do? · Portable and restore"), () =>
            MessageBox.Show(window, L("El modo portátil guarda tus datos junto al ejecutable tras reiniciar. Al restaurar una copia puedes elegir categorías. La app guarda una copia previa para deshacer la restauración.",
                "Portable mode stores data beside the executable after restarting. Restore lets you choose categories. The app saves a snapshot so you can undo the restore."), Text));

        var language = Choice(style, L("Idioma del panel", "Dashboard language"),
            ["Español", "English"], English ? 1 : 0);
        var discordLanguage = Choice(discordTab, L("Idioma de la actividad", "Activity language"),
            ["Español", "English"], preferences.DiscordLanguage == "en" ? 1 : 0);
        var theme = Choice(style, L("Tema", "Theme"),
            [L("Igual que Windows", "Follow Windows"), L("Día", "Day"), L("Noche", "Night")],
            preferences.Theme == "day" ? 1 : preferences.Theme == "night" ? 2 : 0);
        AddRows(style, TextLabel(L("Tema nativo de Windows · Requiere reiniciar", "Native Windows theme · Requires restart"), 9));
        var swatches = ToolBar();
        foreach (var (name, palette) in new[] { (L("Claro", "Light"), ShellPalette.Day), (L("Oscuro", "Dark"), ShellPalette.Dark) })
        {
            var swatch = TextLabel($"  {name}   [ {L("Botón", "Button")} ]  ", 10, true);
            swatch.BackColor = palette.Surface;
            swatch.ForeColor = palette.Text;
            swatch.BorderStyle = BorderStyle.FixedSingle;
            swatches.Controls.Add(swatch);
        }
        AddRows(style, swatches);
        var textSample = TextLabel("", 10);
        var textExample = Grid(1);
        textExample.Padding = new Padding(8);
        textExample.BackColor = shellPalette.Hover;
        var previewButton = MakeButton(L("Botón de ejemplo", "Example button"));
        previewButton.Enabled = false;
        previewButton.TabStop = false;
        AddRows(textExample, textSample, previewButton);
        AddRows(style, textExample);
        Font? sampleFont = null;
        textExample.Disposed += (_, _) => sampleFont?.Dispose();
        void ScaleSample()
        {
            var value = textSizes[Math.Max(0, textSize.SelectedIndex)];
            var newFont = new Font("Segoe UI", 10f * value / 100);
            textSample.Font = previewButton.Font = newFont;
            sampleFont?.Dispose();
            sampleFont = newFont;
            textSample.Text = L($"Texto al {value}% · Destino | Distancia", $"Text at {value}% · Destination | Distance");
        }
        textSize.SelectedIndexChanged += (_, _) => ScaleSample();
        window.Shown += (_, _) => ScaleSample();
        var compact = Check(style, L("Vista compacta", "Compact view"), preferences.Compact);
        var showArt = Check(style, L("Mostrar ilustración", "Show artwork"), preferences.ShowArtwork);
        var showGuide = Check(style, L("Mostrar guía de conexión", "Show connection guide"), !preferences.GuideDismissed);
        var tripFields = new Dictionary<string, CheckBox>();
        foreach (var (key, title) in new[]
        {
            ("cargo", L("Mostrar carga", "Show cargo")), ("route", L("Mostrar ruta", "Show route")),
            ("location", L("Mostrar ciudad cercana", "Show nearby city")),
            ("metrics", L("Mostrar distancia, ETA y progreso", "Show distance, ETA and progress")),
            ("timeline", L("Mostrar etapas del viaje", "Show trip stages"))
        })
            tripFields[key] = Check(style, title, preferences.HiddenPanels?.Contains(key) != true);
        var orderPanels = MakeOrder(style, L("Orden y visibilidad del panel", "Dashboard order and visibility"),
            OrderPanels(preferences, panelKeys), PanelName, key => preferences.HiddenPanels?.Contains(key) != true);
        var startup = Check(game, L("Iniciar minimizada con Windows", "Start minimized with Windows"), DesktopServices.StartsWithWindows());
        var localDemo = Check(game, L("Demostración local sin publicar", "Local demo without publishing"), demo);

        var share = Check(privacy, L("Publicar actividad en Discord", "Publish activity to Discord"), preferences.Sharing);
        var shareState = TextLabel("", 9, true);
        AddRows(privacy, shareState);
        var privacyNote = TextLabel(L("◈ La ubicación se comparte según el perfil y los campos seleccionados.",
            "◈ Location is shared according to the profile and selected fields."), 10);
        privacyNote.Padding = new Padding(8);
        privacyNote.BorderStyle = BorderStyle.FixedSingle;
        AddRows(privacy, privacyNote);
        foreach (var (title, description) in new[]
        {
            (L("Normal", "Normal"), L("Muestra los datos que has elegido en Ajustes de Discord.", "Shows the fields selected in Discord settings.")),
            ("Stream", L("Muestra la actividad general y el país cuando está permitido.", "Shows general activity and the country when allowed.")),
            (L("Privado", "Private"), L("Muestra el estado básico del juego.", "Shows the basic game activity."))
        })
        {
            var name = TextLabel(title, 10, true);
            name.Margin = new Padding(0, 10, 0, 1);
            AddRows(privacy, name, TextLabel(description, 9));
        }
        var profile = Choice(privacy, L("Perfil de publicación", "Sharing profile"),
            [L("Personalizado", "Custom"), L("Completa", "Full"), L("Solo ruta", "Route only"), L("Privada", "Private")],
            preferences.Profile switch { "full" => 1, "route" => 2, "private" => 3, _ => 0 });
        var initialFields = PresenceFormatter.Order(preferences);
        bool EnabledField(string key) => key switch
        {
            "cargo" => preferences.ShowCargo, "route" => preferences.ShowRoute,
            "city" => preferences.ShowCity, _ => preferences.ShowDistance
        };
        var fieldLayout = Grid(2);
        fieldLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        fieldLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var fieldOptions = Grid(1);
        var previewOptions = Grid(1);
        var fields = MakeOrder(fieldOptions, L("Datos de Discord en orden de aparición", "Discord fields in display order"),
            initialFields, FieldName, EnabledField);
        var livePreview = TextLabel("", 11);
        var previewPicture = new PictureBox { Size = new Size(112, 112), SizeMode = PictureBoxSizeMode.Zoom,
            AccessibleName = L("Portada de ejemplo", "Example artwork"), TabStop = false };
        string? previewPictureKey = null;
        window.Disposed += (_, _) => previewPicture.Image?.Dispose();
        previewOptions.Padding = new Padding(12);
        previewOptions.BackColor = shellPalette.Hover;
        var examples = Choice(previewOptions, L("Vista previa", "Preview"),
            [L("Conducción libre", "Free drive"), L("Entrega", "Delivery"), L("Pasajeros", "Passengers"),
                L("Mapa", "Map"), L("Modo foto", "Photo mode")], 1);
        var exampleProfile = TextLabel("", 9, true);
        var hiddenFields = TextLabel("", 9);
        AddRows(privacy, hiddenFields);
        var coverComparison = MakeButton(L("Comparar portadas ES / EN", "Compare ES / EN artwork"));
        coverComparison.Anchor = AnchorStyles.Left;
        coverComparison.Click += (_, _) => ShowArtworkLanguages(window, examples.SelectedIndex == 2);
        var coverSamples = ToolBar();
        var truckCover = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = L("Portada de camión", "Truck cover") };
        var coachCover = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = L("Portada de autobús", "Coach cover") };
        coverSamples.Controls.AddRange([truckCover, coachCover]);
        string? coversLanguage = null;
        window.Disposed += (_, _) => { truckCover.Image?.Dispose(); coachCover.Image?.Dispose(); };
        AddRows(previewOptions, previewPicture, livePreview, exampleProfile, coverSamples, coverComparison);
        fieldLayout.Controls.Add(fieldOptions, 0, 0);
        fieldLayout.Controls.Add(previewOptions, 1, 0);
        AddRows(discordTab, fieldLayout);
        discordTab.SuspendLayout();
        foreach (Control control in discordTab.Controls.Cast<Control>().Where(control => control != fieldLayout)
            .OrderByDescending(control => discordTab.GetRow(control)))
        {
            var row = discordTab.GetRow(control);
            if (row >= 1) discordTab.SetRow(control, row + 1);
        }
        discordTab.SetRow(fieldLayout, 1);
        discordTab.ResumeLayout();
        void SizePreview()
        {
            var available = Math.Max(240, tabs.DisplayRectangle.Width - discordTab.Padding.Horizontal - 24);
            var stacked = available < 500 * Math.Max(100, preferences.TextPercent) / 100;
            fieldLayout.SuspendLayout();
            LimitPanelWidth(fieldLayout, available);
            SetColumnWidth(fieldLayout.ColumnStyles[0], stacked ? available : (int)(available * .56));
            SetColumnWidth(fieldLayout.ColumnStyles[1], stacked ? 0 : (int)(available * .43));
            fieldLayout.SetCellPosition(previewOptions, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
            fieldLayout.SetColumnSpan(fieldOptions, stacked ? 2 : 1);
            fieldLayout.SetColumnSpan(previewOptions, stacked ? 2 : 1);
            previewOptions.Margin = stacked ? new Padding(0, 8, 0, 0) : new Padding(12, 0, 0, 0);
            var previewWidth = (stacked ? available : (int)(available * .44)) - 36;
            foreach (var label in previewOptions.Controls.OfType<Label>()) SetWrap(label, previewWidth);
            foreach (var label in fieldOptions.Controls.OfType<Label>())
                SetWrap(label, (stacked ? available : (int)(available * .56)) - 16);
            fieldLayout.ResumeLayout();
        }
        tabs.ClientSizeChanged += (_, _) => SizePreview();
        window.Shown += (_, _) => SizePreview();

        AppSettings ReadDiscord() => preferences with
        {
            Sharing = share.Checked,
            ShowCargo = IsChecked(fields, "cargo"),
            ShowRoute = IsChecked(fields, "route"),
            ShowCity = IsChecked(fields, "city"),
            ShowCountry = country.Checked,
            ShowDistance = IsChecked(fields, "distance"),
            PresenceOrder = KeysOf(fields),
            DiscordLanguage = discordLanguage.SelectedIndex == 1 ? "en" : "es",
            Profile = profile.SelectedIndex switch { 1 => "full", 2 => "route", 3 => "private", _ => "custom" },
            Options = getFeatures()
        };

        void Preview()
        {
            var config = ReadDiscord();
            shareState.Text = share.Checked ? L("Activada", "On") : L("Desactivada", "Off");
            imageState.Text = image.Checked ? L("Activada", "On") : L("Desactivada", "Off");
            var sample = examples.SelectedIndex switch
            {
                0 => Sample with { OnJob = false, RouteDistanceMeters = 0 },
                2 => Sample with { Coach = true, Cargo = "" },
                _ => Sample
            };
            var visible = PresencePrivacy.Apply(config, sample.OnJob, false);
            var english = config.DiscordLanguage == "en";
            var presence = examples.SelectedIndex is 3 or 4
                ? new PresenceText(PresenceFormatter.ScreenTitle(examples.SelectedIndex == 3 ? GameScreen.Map : GameScreen.Photo, false, english), "")
                : PresenceFormatter.Format(sample, visible.Features.DiscordMode == "stream" ? null : "Chambéry", visible, "FR");
            livePreview.Text = config.Sharing ? $"{DiscordIdentity.ActivityName}\n{presence.Details}\n{presence.State}"
                : L("Ejemplo · Publicación en pausa", "Example · Publishing paused");
            if (config.Sharing && activityTimer.Checked && config.Features.DiscordMode != "private")
                livePreview.Text += L("\nTiempo transcurrido · 03:00", "\nElapsed · 03:00");
            var hidden = new[] { (visible.ShowCargo, "cargo"), (visible.ShowRoute, "route"),
                (visible.ShowCity, "city"), (visible.ShowDistance, "distance") }
                .Where(field => !field.Item1).Select(field => FieldName(field.Item2)).ToArray();
            hiddenFields.Text = !config.Sharing ? L("Publicación desactivada", "Publishing off")
                : hidden.Length == 0 ? L("Todos los campos están habilitados", "All fields are enabled")
                : L("Se oculta · ", "Hidden · ") + string.Join(", ", hidden);
            var picture = config.Sharing && image.Checked && config.Features.DiscordMode != "private"
                ? DiscordIdentity.ImageFor(visible.SharesCountry ? "FR" : null, releaseArtwork, english, sample.Coach) : null;
            exampleProfile.Text = L("Perfil · ", "Profile · ") + (config.Features.DiscordMode switch
            {
                "stream" => "Stream", "private" => L("Privado", "Private"), _ => "Normal"
            });
            if (coversLanguage != config.DiscordLanguage)
            {
                coversLanguage = config.DiscordLanguage;
                truckCover.Image?.Dispose();
                coachCover.Image?.Dispose();
                truckCover.Image = LoadPreviewImage(DiscordIdentity.ImageFor("FR", releaseArtwork, english));
                coachCover.Image = LoadPreviewImage(DiscordIdentity.ImageFor("FR", releaseArtwork, english, coach: true));
            }
            coverSamples.Visible = coverComparison.Visible = image.Checked;
            livePreview.AccessibleDescription = L("Vista previa de ejemplo de la actividad de Discord.", "Example preview of Discord activity.");
            if (picture != previewPictureKey)
            {
                previewPictureKey = picture;
                previewPicture.Image?.Dispose();
                previewPicture.Image = LoadPreviewImage(picture);
            }
            previewPicture.Visible = previewPicture.Image is not null;
        }

        var changingProfile = false;
        profile.SelectedIndexChanged += (_, _) =>
        {
            if (profile.SelectedIndex == 0) return;
            changingProfile = true;
            var selected = profile.SelectedIndex;
            share.Checked = selected != 3;
            country.Checked = selected == 1;
            var keys = selected == 2 ? new[] { "route", "cargo", "city", "distance" } : PresenceFormatter.Fields;
            fields.Items.Clear();
            foreach (var key in keys) fields.Items.Add(new OrderedItem(key, FieldName(key)), selected == 1 || selected == 2 && key == "route");
            changingProfile = false;
            Preview();
        };
        void ChangedFields()
        {
            if (!changingProfile) profile.SelectedIndex = 0;
            Preview();
        }
        share.CheckedChanged += (_, _) => ChangedFields();
        fields.ItemCheck += (_, _) => { if (!changingProfile) window.BeginInvoke(ChangedFields); };
        fields.SelectedIndexChanged += (_, _) => { if (!changingProfile) Preview(); };
        discordLanguage.SelectedIndexChanged += (_, _) => Preview();
        image.CheckedChanged += (_, _) => Preview();
        examples.SelectedIndexChanged += (_, _) => Preview();
        activityTimer.CheckedChanged += (_, _) => Preview();
        country.CheckedChanged += (_, _) => Preview();
        tabs.SelectedIndexChanged += (_, _) => Preview();
        Preview();

        AddRows(game, TextLabel(L($"Cobertura del catálogo activo · {cities.Cities.Count(city => city.Country is not null)}/{cities.Cities.Count} ciudades con país",
            $"Active catalog coverage · {cities.Cities.Count(city => city.Country is not null)}/{cities.Cities.Count} cities with a country"), 9));
        var catalogs = Path.Combine(TripJournal.DataFolder, "catalogs");
        AddRows(game, TextLabel(activeCatalog is null
            ? L($"Catálogo incluido · {cities.Cities.Count} ciudades", $"Built-in catalog · {cities.Cities.Count} cities")
            : L($"Catálogo activo · {activeCatalog} · {cities.Cities.Count} ciudades", $"Active catalog · {activeCatalog} · {cities.Cities.Count} cities"), 10));
        var catalog = Choice(game, L("Catálogo de ciudades", "City catalog"), [L("Incluido · ETS2", "Built-in · ETS2")], 0);
        void ListCatalogs(string? selected)
        {
            while (catalog.Items.Count > 1) catalog.Items.RemoveAt(1);
            if (Directory.Exists(catalogs))
                catalog.Items.AddRange(Directory.EnumerateFiles(catalogs, "*.json").Select(Path.GetFileName).OfType<string>().Order().Cast<object>().ToArray());
            catalog.SelectedIndex = selected is not null && catalog.Items.Contains(selected) ? catalog.Items.IndexOf(selected) : 0;
        }
        ListCatalogs(preferences.CityCatalog);
        AddAction(game, L("Importar catálogo JSON…", "Import JSON catalog…"), () =>
        {
            using var picker = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (picker.ShowDialog(window) != DialogResult.OK) return;
            try
            {
                using var file = File.OpenRead(picker.FileName);
                if (file.Length > 5_000_000) throw new InvalidDataException();
                var imported = new CityLookup(file);
                var report = L($"Ciudades válidas: {imported.Cities.Count}\nRechazadas: {imported.Rejected}\nPaíses reconocidos: {imported.RecognizedCountries}\nCon gameName: {imported.Identified}\nIdentificadores duplicados: {imported.DuplicateIds}\nPosiciones duplicadas: {imported.DuplicatePositions}",
                    $"Valid cities: {imported.Cities.Count}\nRejected: {imported.Rejected}\nRecognized countries: {imported.RecognizedCountries}\nWith gameName: {imported.Identified}\nDuplicate identifiers: {imported.DuplicateIds}\nDuplicate positions: {imported.DuplicatePositions}");
                if (imported.DuplicateIds + imported.DuplicatePositions > 0)
                {
                    MessageBox.Show(window, report + "\n\n" + L("Corrige los duplicados antes de importar.", "Fix duplicates before importing."), Text);
                    return;
                }
                if (!Confirm(window, report + "\n\n" + L("¿Importar el catálogo?", "Import the catalog?"))) return;
                file.Position = 0;
                Directory.CreateDirectory(catalogs);
                var name = $"{Path.GetFileNameWithoutExtension(picker.FileName)}-{Guid.NewGuid():N}"[..Math.Min(Path.GetFileNameWithoutExtension(picker.FileName).Length + 9, 80)] + ".json";
                using (var output = new FileStream(Path.Combine(catalogs, name), FileMode.CreateNew)) file.CopyTo(output);
                ListCatalogs(name);
            }
            catch (Exception ex) when (IsCatalogError(ex))
            {
                MessageBox.Show(window, L("Usa un JSON con citiesList y campos realName, x y z.", "Use a JSON file with citiesList and realName, x and z fields."), Text);
            }
        });
        AddAction(game, L("Buscar ETS2…", "Locate ETS2…"), LocateGame);
        AddAction(game, L("Reparar lector", "Repair reader"), () => MaintainReader(false));
        AddAction(game, L("Retirar lector", "Remove reader"), () => MaintainReader(true));
        AddRows(game, TextLabel(L("Diagnóstico del lector", "Reader diagnostics"), 14, true));
        if (setupError is { Length: > 0 } currentError)
            AddRows(game, TextLabel(currentError, 10));
        AddAction(game, L("Copiar diagnóstico", "Copy diagnostics"), CopyDiagnostics);
        AddAction(game, L("Licencias", "Licenses"), ShowLicenses);
        AddAction(game, L("Versión y compatibilidad del lector…", "Reader version and compatibility…"), () => ShowReader(window));
        AddAction(game, L("¿Qué hace esto? · Lector y versiones", "What does this do? · Reader and versions"), () =>
            MessageBox.Show(window, L("El lector permite que ETS2 envíe los datos del viaje. Cierra ETS2 antes de repararlo o retirarlo. Volver a una versión anterior sustituye el ejecutable por la copia guardada al actualizar.",
                "The reader lets ETS2 send trip data. Close ETS2 before repairing or removing it. Rolling back replaces the executable with the copy kept during an update."), Text));

        apply.Click += (_, _) =>
        {
            try
            {
                if (startup.Checked != DesktopServices.StartsWithWindows()) DesktopServices.SetStartup(startup.Checked);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                MessageBox.Show(window, L("Windows no permitió cambiar el inicio automático.", "Windows did not allow changing startup."), Text);
                return;
            }
            var compactChanged = preferences.Compact != compact.Checked;
            string ThemeValue() => theme.SelectedIndex switch { 1 => "day", 2 => "night", _ => "system" };
            demo = localDemo.Checked;
            SavePreferences(ReadDiscord() with
            {
                Language = language.SelectedIndex == 1 ? "en" : "es",
                Theme = ThemeValue(),
                PreviewUpdates = updateChannel.SelectedIndex == 1,
                TextPercent = textSizes[textSize.SelectedIndex],
                FuelThreshold = thresholds[fuel.SelectedIndex],
                PrivacyShortcut = shortcuts.SelectedIndex,
                RetentionDays = retentionValues[retention.SelectedIndex],
                ActivityImage = image.Checked,
                ActivityTimer = activityTimer.Checked,
                ShowArtwork = showArt.Checked,
                Compact = compact.Checked,
                GuideDismissed = !showGuide.Checked,
                CityCatalog = catalog.SelectedIndex > 0 ? catalog.SelectedItem as string : null,
                PanelOrder = KeysOf(orderPanels).SequenceEqual(panelKeys) ? null : KeysOf(orderPanels),
                HiddenPanels = KeysOf(orderPanels).Where(key => !IsChecked(orderPanels, key))
                    .Concat(tripFields.Where(field => !field.Value.Checked).Select(field => field.Key))
                    .ToArray()
            });
            LoadCatalog();
            ResetLocation(IsGameRunning());
            ApplyLanguage();
            RegisterPrivacyShortcut();
            if ((preferences.PrivacyShortcut > 0 && !hotkeyRegistered) || (preferences.Features.PauseShortcut && !pauseHotkeyRegistered))
                MessageBox.Show(window, L("Windows no pudo registrar el atajo. Prueba otra combinación.", "Windows could not register the shortcut. Try another combination."), Text);
            ScaleText(this);
            ResizeShell();
            if (compactChanged && !fullScreen) ApplyCompactSize();
            ConfigurePanels();
            RefreshPresence();
            window.DialogResult = DialogResult.OK;
        };
        var initialPage = initialTab switch
        {
            "display" => style.Parent, "behavior" => privacy.Parent, "advanced" => game.Parent, _ => discordTab.Parent
        };
        if (initialPage is TabPage page) tabs.SelectedTab = page;
        StyleTool(window);
        window.ShowDialog(this);
    }

    private void ShowArtworkLanguages(IWin32Window owner, bool coach)
    {
        using var window = ToolWindow(L("Portadas por idioma", "Artwork by language"), new Size(460, 320));
        var layout = Grid(2);
        layout.Padding = new Padding(16);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var column = 0; column < 2; column++)
        {
            var english = column == 1;
            var artwork = LoadPreviewImage(DiscordIdentity.ImageFor("FR", english: english, coach: coach));
            var image = new PictureBox { Size = new Size(128, 128), Image = artwork, SizeMode = PictureBoxSizeMode.Zoom,
                AccessibleName = english ? "English artwork" : "Portada en español", TabStop = false };
            image.Disposed += (_, _) => artwork?.Dispose();
            var sample = Grid(1);
            AddRows(sample, TextLabel(english ? "English" : "Español", 11, true), image);
            layout.Controls.Add(sample, column, 0);
        }
        window.Controls.Add(layout);
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private bool portableRestartPending;

    private void RestartApplication(IWin32Window owner)
    {
        if ((recording is not null || liveDelivery) && !Confirm(owner, L("¿Reiniciar ahora? Se interrumpirá el seguimiento de este viaje.",
            "Restart now? Tracking for this trip will be interrupted."))) return;
        Application.Restart();
    }

    private TableLayoutPanel AddTab(TabControl tabs, string name)
    {
        var page = new TabPage(name) { AutoScroll = true, BackColor = Background };
        var content = Grid(1);
        content.Padding = new Padding(17);
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
        void WrapLabels()
        {
            var width = page.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            foreach (var label in content.Controls.OfType<Label>()) SetWrap(label, width - label.Margin.Horizontal);
        }
        page.ClientSizeChanged += (_, _) => WrapLabels();
        page.Enter += (_, _) => WrapLabels();
        return content;
    }

    private static ComboBox Choice(TableLayoutPanel parent, string title, string[] items, int selected)
    {
        var box = new ComboBox { Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = title };
        box.Items.AddRange(items);
        box.SelectedIndex = selected;
        AddRows(parent, TextLabel(title, 10, true), box);
        void SizeChoice()
        {
            var row = parent.RowStyles[parent.GetRow(box)];
            var height = Math.Max(box.Height, box.PreferredHeight) + box.Margin.Vertical;
            if (row.SizeType != SizeType.Absolute) row.SizeType = SizeType.Absolute;
            if (row.Height != height) row.Height = height;
        }
        box.SizeChanged += (_, _) => SizeChoice();
        box.FontChanged += (_, _) => SizeChoice();
        SizeChoice();
        return box;
    }

    private static CheckBox Check(TableLayoutPanel parent, string title, bool value)
    {
        var box = new CheckBox { Text = title, Checked = value, AutoSize = true, FlatStyle = FlatStyle.Flat,
            CheckAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 4, 0, 4) };
        AddRows(parent, box);
        return box;
    }

    private static void AddAction(TableLayoutPanel parent, string text, Action action)
    {
        var button = MakeButton(text);
        button.Anchor = AnchorStyles.Left;
        button.Click += (_, _) => action();
        AddRows(parent, button);
    }

    private sealed record OrderedItem(string Key, string Title)
    {
        public override string ToString() => Title;
    }

    private CheckedListBox MakeOrder(TableLayoutPanel parent, string title, string[] keys,
        Func<string, string> titleFor, Func<string, bool> checkedFor)
    {
        var list = new CheckedListBox
        {
            Dock = DockStyle.Top, Height = 94, IntegralHeight = false, CheckOnClick = true,
            AccessibleName = title, BackColor = Surface, ForeColor = Foreground
        };
        void SizeOrder() => list.Height = Math.Max(94, keys.Length * list.ItemHeight + 8);
        list.FontChanged += (_, _) => SizeOrder();
        SizeOrder();
        foreach (var key in keys) list.Items.Add(new OrderedItem(key, titleFor(key)), checkedFor(key));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        foreach (var direction in new[] { -1, 1 })
        {
            var button = MakeButton(direction == -1 ? L("↑ Subir", "↑ Move up") : L("↓ Bajar", "↓ Move down"));
            button.Click += (_, _) =>
            {
                var from = list.SelectedIndex;
                var to = from + direction;
                if (from < 0 || to < 0 || to >= list.Items.Count) return;
                var item = list.Items[from];
                var enabled = list.GetItemChecked(from);
                list.Items.RemoveAt(from);
                list.Items.Insert(to, item);
                list.SetItemChecked(to, enabled);
                list.SelectedIndex = to;
            };
            buttons.Controls.Add(button);
        }
        AddRows(parent, TextLabel(title, 10, true), list, buttons);
        return list;
    }

    private static string[] KeysOf(CheckedListBox list) => list.Items.OfType<OrderedItem>().Select(item => item.Key).ToArray();
    private static bool IsChecked(CheckedListBox list, string key) =>
        list.CheckedItems.OfType<OrderedItem>().Any(item => item.Key == key);
}
