namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ShowSettings(string initialTab = "")
    {
        using var window = ToolWindow(L("Ajustes", "Settings"), new Size(740, 560));
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var apply = MakeButton(L("Guardar cambios", "Save changes"));
        var cancel = MakeButton(L("Cancelar", "Cancel"));
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange([apply, cancel]);
        window.Controls.Add(tabs);
        window.Controls.Add(footer);
        window.AcceptButton = apply;
        window.CancelButton = cancel;

        var discordTab = AddTab(tabs, "Discord");
        var privacy = AddTab(tabs, L("Privacidad", "Privacy"));
        var game = AddTab(tabs, L("Juego", "Game"));
        var style = AddTab(tabs, L("Apariencia", "Appearance"));
        var data = AddTab(tabs, L("Datos locales", "Local data"));
        int[] textSizes = [85, 100, 115, 135];
        var getFeatures = BuildFeatureSettings(window, tabs, style, discordTab, privacy, game, data);
        AddSettingsSearch(window, tabs);
        var textSize = Choice(style, L("Tamaño del texto", "Text size"), textSizes.Select(value => $"{value}%").ToArray(),
            Math.Max(0, Array.IndexOf(textSizes, preferences.TextPercent)));
        var shortcuts = Choice(privacy, L("Atajo global para ocultar el destino", "Global shortcut to hide the destination"),
            [L("Desactivado", "Off"), "Ctrl + Alt + F8", "Ctrl + Alt + F9", "Ctrl + Alt + F10"], Math.Clamp(preferences.PrivacyShortcut, 0, 3));
        var image = Check(discordTab, L("Mostrar imagen del juego", "Show game artwork"), preferences.ActivityImage);
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
        AddAction(data, LocalData.IsPortable ? L("Volver a guardar en Windows…", "Use the Windows profile…")
            : L("Activar modo portátil…", "Enable portable mode…"), () => ChangeStorage(window));
        AddAction(data, L("Exportar copia de seguridad…", "Export backup…"), () => ExportBackup(window));
        AddAction(data, L("Restaurar copia de seguridad…", "Restore backup…"), () => RestoreBackup(window));

        var language = Choice(style, L("Idioma del panel", "Dashboard language"),
            ["Español", "English"], English ? 1 : 0);
        var discordLanguage = Choice(discordTab, L("Idioma de la actividad", "Activity language"),
            ["Español", "English"], preferences.DiscordLanguage == "en" ? 1 : 0);
        var theme = Choice(style, L("Tema", "Theme"),
            [L("Igual que Windows", "Follow Windows"), L("Día", "Day"), L("Noche", "Night")],
            preferences.Theme == "day" ? 1 : preferences.Theme == "night" ? 2 : 0);
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
        var profile = Choice(privacy, L("Perfil de publicación", "Sharing profile"),
            [L("Personalizado", "Custom"), L("Completa", "Full"), L("Solo ruta", "Route only"), L("Privada", "Private")],
            preferences.Profile switch { "full" => 1, "route" => 2, "private" => 3, _ => 0 });
        var initialFields = PresenceFormatter.Order(preferences);
        bool EnabledField(string key) => key switch
        {
            "cargo" => preferences.ShowCargo, "route" => preferences.ShowRoute,
            "city" => preferences.ShowCity, _ => preferences.ShowDistance
        };
        var fields = MakeOrder(discordTab, L("Datos de Discord en orden de aparición", "Discord fields in display order"),
            initialFields, FieldName, EnabledField);
        var livePreview = TextLabel("", 11, true);
        livePreview.Margin = new Padding(0, 5, 0, 5);
        AddRows(discordTab, livePreview);

        AppSettings ReadDiscord() => preferences with
        {
            Sharing = share.Checked,
            ShowCargo = IsChecked(fields, "cargo"),
            ShowRoute = IsChecked(fields, "route"),
            ShowCity = IsChecked(fields, "city"),
            ShowDistance = IsChecked(fields, "distance"),
            PresenceOrder = KeysOf(fields),
            DiscordLanguage = discordLanguage.SelectedIndex == 1 ? "en" : "es",
            Profile = profile.SelectedIndex switch { 1 => "full", 2 => "route", 3 => "private", _ => "custom" }
        };

        void Preview()
        {
            var config = ReadDiscord();
            var presence = PresenceFormatter.Format(Sample, "Chambéry", config);
            livePreview.Text = config.Sharing ? $"{DiscordIdentity.ActivityName}\n{presence.Details}\n{presence.State}"
                : L("Ejemplo · Publicación en pausa", "Example · Publishing paused");
        }

        var changingProfile = false;
        profile.SelectedIndexChanged += (_, _) =>
        {
            if (profile.SelectedIndex == 0) return;
            changingProfile = true;
            var selected = profile.SelectedIndex;
            share.Checked = selected != 3;
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
        Preview();

        var catalogs = Path.Combine(TripJournal.DataFolder, "catalogs");
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
                _ = new CityLookup(file);
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
        AddAction(game, L("Copiar diagnóstico", "Copy diagnostics"), CopyDiagnostics);
        AddAction(game, L("Licencias", "Licenses"), ShowLicenses);
        AddAction(game, L("Versión y compatibilidad del lector…", "Reader version and compatibility…"), () => ShowReader(window));

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
                TextPercent = textSizes[textSize.SelectedIndex],
                FuelThreshold = thresholds[fuel.SelectedIndex],
                PrivacyShortcut = shortcuts.SelectedIndex,
                RetentionDays = retentionValues[retention.SelectedIndex],
                ActivityImage = image.Checked,
                ActivityTimer = activityTimer.Checked,
                ShowArtwork = showArt.Checked,
                Options = getFeatures(),
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

    private TableLayoutPanel AddTab(TabControl tabs, string name)
    {
        var page = new TabPage(name) { AutoScroll = true, BackColor = Background };
        var content = Grid(1);
        content.Padding = new Padding(17);
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
        return content;
    }

    private static ComboBox Choice(TableLayoutPanel parent, string title, string[] items, int selected)
    {
        var box = new ComboBox { Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = title };
        box.Items.AddRange(items);
        box.SelectedIndex = selected;
        AddRows(parent, TextLabel(title, 10, true), box);
        return box;
    }

    private static CheckBox Check(TableLayoutPanel parent, string title, bool value)
    {
        var box = new CheckBox { Text = title, Checked = value, AutoSize = true, FlatStyle = FlatStyle.Flat };
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
