namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ShowHistory()
    {
        using var window = ToolWindow(L("Diario de entregas", "Delivery journal"), new Size(980, 590));
        var header = Grid(1);
        header.Padding = new Padding(14, 12, 14, 6);
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        var cityFilter = new TextBox { Width = 160, PlaceholderText = L("Ciudad, carga o etiqueta", "City, cargo or tag"), AccessibleName = L("Buscar en el diario", "Search the journal") };
        var from = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 123, AccessibleName = L("Desde", "From") };
        var to = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 123, AccessibleName = L("Hasta", "To") };
        var export = MakeButton(L("Exportar CSV", "Export CSV"));
        var favorites = new CheckBox { Text = L("Solo favoritos", "Favorites only"), AutoSize = true };
        var tools = ToolBar();
        var driver = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = L("Conductor", "Driver") };
        var addDriver = MakeButton(L("Añadir conductor", "Add driver"));
        var calendar = MakeButton(L("Calendario", "Calendar"));
        var compare = MakeButton(L("Comparar dos viajes", "Compare two trips"));
        var notes = MakeButton(L("Notas y favorito", "Notes and favorite"));
        var remove = MakeButton(L("Eliminar viaje", "Delete trip"));
        compare.AccessibleDescription = L("Selecciona dos filas con Ctrl para comparar.", "Select two rows with Ctrl to compare.");
        var report = MakeButton(L("Informe mensual", "Monthly report"));
        var move = MakeButton(L("Mover viaje", "Move trip"));
        var trash = MakeButton(L("Papelera", "Trash"));
        tools.Controls.AddRange([driver, addDriver, calendar, compare, notes, remove, report, move, trash]);
        void ListDrivers()
        {
            driver.Items.Clear();
            driver.Items.Add(new DriverOption("", L("Conductor principal", "Main driver")));
            foreach (var name in (preferences.Drivers ?? []).Append(preferences.DriverProfile)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
                driver.Items.Add(new DriverOption(name, name));
            driver.SelectedItem = driver.Items.OfType<DriverOption>().First(option =>
                string.Equals(option.Key, preferences.DriverProfile, StringComparison.OrdinalIgnoreCase));
        }
        ListDrivers();
        filters.Controls.AddRange([cityFilter, new Label { Text = L("Desde", "From"), AutoSize = true }, from,
            new Label { Text = L("Hasta", "To"), AutoSize = true }, to, favorites, export]);
        var stats = TextLabel("", 11, true);
        AddRows(header, TextLabel(L("Tus viajes registrados", "Your recorded trips"), 18, true), stats, tools, filters);

        var history = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Surface, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        history.DefaultCellStyle.BackColor = Surface;
        history.DefaultCellStyle.ForeColor = Foreground;
        history.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        history.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var (key, label, size) in new[]
        {
            ("date", L("Fecha", "Date"), 100), ("origin", L("Origen", "Origin"), 90),
            ("destination", L("Destino", "Destination"), 90), ("cargo", L("Carga", "Cargo"), 145),
            ("result", L("Resultado", "Result"), 86), ("km", preferences.Features.Miles ? "mi" : "km", 55), ("time", L("Al volante", "Driving"), 75)
        })
        {
            var column = new DataGridViewTextBoxColumn { Name = key, HeaderText = label, FillWeight = size, SortMode = DataGridViewColumnSortMode.NotSortable };
            history.Columns.Add(column);
        }
        var entries = Array.Empty<TripEntry>();
        void RefreshHistory()
        {
            var search = cityFilter.Text.Trim();
            entries = journal.Entries.Where(entry => entry.Finished is { } finish
                && (!from.Checked || finish.LocalDateTime.Date >= from.Value.Date)
                && (!to.Checked || finish.LocalDateTime.Date <= to.Value.Date)
                && (!favorites.Checked || entry.Favorite)
                && new[] { entry.Origin, entry.Destination, entry.Cargo, entry.Tags, entry.Notes }
                    .Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true))
                .OrderByDescending(entry => entry.Finished).ToArray();
            history.Rows.Clear();
            foreach (var entry in entries)
            {
                var row = history.Rows.Add((entry.Favorite ? "★ " : "") + entry.Finished?.ToLocalTime().ToString("g"),
                    entry.Origin, entry.Destination, entry.Cargo, Result(entry), $"{(preferences.Features.Miles ? entry.Km / 1.609344 : entry.Km):N1}", FormatDriving(entry.DrivingSeconds));
                history.Rows[row].Tag = entry;
            }
            var completed = entries.Where(entry => entry.Result == "delivered").ToArray();
            var topRoute = completed.GroupBy(entry => $"{entry.Origin} → {entry.Destination}")
                .OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
            stats.Text = entries.Length == 0 ? L("Todavía no hay viajes para este filtro.", "No trips match this filter yet.")
                : $"{completed.Length} {L("entregadas", "delivered")}  ·  {entries.Length - completed.Length} {L("canceladas", "cancelled")}  ·  "
                    + $"{AppOptions.Distance(entries.Sum(entry => entry.Km), preferences.Features.Miles, "N1")}  ·  {FormatDriving(entries.Sum(entry => entry.DrivingSeconds))} {L("al volante", "driving")}"
                    + (topRoute is null ? "" : $"\n{L("Ruta más frecuente", "Most frequent route")} · {topRoute}");
            export.Enabled = entries.Length > 0;
            UpdateActions();
        }
        TripEntry[] Selection() => history.SelectedRows.Cast<DataGridViewRow>().OrderBy(row => row.Index)
            .Select(row => row.Tag).OfType<TripEntry>().ToArray();
        void UpdateActions()
        {
            var count = Selection().Length;
            notes.Enabled = remove.Enabled = move.Enabled = count == 1;
            compare.Enabled = count == 2;
        }
        history.SelectionChanged += (_, _) => UpdateActions();
        favorites.CheckedChanged += (_, _) => RefreshHistory();
        driver.SelectionChangeCommitted += (_, _) =>
        {
            if (driver.SelectedItem is DriverOption option)
                SwitchDriver(option.Key, preferences.Drivers ?? [], window);
            ListDrivers();
            RefreshHistory();
        };
        addDriver.Click += (_, _) =>
        {
            var name = AskDriverName(window);
            if (name is null) return;
            SwitchDriver(name, (preferences.Drivers ?? []).Append(name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), window);
            ListDrivers();
            RefreshHistory();
        };
        calendar.Click += (_, _) => ShowWeek(window);
        report.Click += (_, _) => ShowMonthly(window);
        move.Click += (_, _) =>
        {
            if (Selection() is [var entry] && MoveTrip(entry, window)) RefreshHistory();
        };
        trash.Click += (_, _) => { ShowTripTrash(window); RefreshHistory(); };
        compare.Click += (_, _) =>
        {
            if (Selection() is [var first, var second]) CompareTrips(first, second, window);
        };
        notes.Click += (_, _) =>
        {
            if (Selection() is [var entry] && EditTripNotes(entry, window)) RefreshHistory();
        };
        remove.Click += (_, _) =>
        {
            if (Selection() is not [var entry] || !Confirm(window, L("¿Enviar este viaje a la papelera?", "Move this trip to the trash?"))) return;
            if (!journal.Remove(entry.Id)) MessageBox.Show(window, L("No se pudo guardar el diario.", "Could not save the journal."), Text);
            if (completedTrip?.Id == entry.Id) completedTrip = null;
            RefreshHistory();
        };
        cityFilter.TextChanged += (_, _) => RefreshHistory();
        from.ValueChanged += (_, _) => RefreshHistory();
        to.ValueChanged += (_, _) => RefreshHistory();
        history.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && history.Rows[e.RowIndex].Tag is TripEntry entry) ShowSummary(entry, window);
        };
        export.Click += (_, _) =>
        {
            using var picker = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "ets2-trips.csv" };
            if (picker.ShowDialog(window) != DialogResult.OK) return;
            try { TripJournal.Export(picker.FileName, entries, English); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(window, L("No se pudo escribir el CSV.", "Could not write the CSV."), Text);
            }
        };
        window.Controls.Add(history);
        window.Controls.Add(header);
        StyleTool(window);
        RefreshHistory();
        window.ShowDialog(this);
    }

    private string Result(TripEntry entry) => entry.Result == "delivered"
        ? L("Entregada", "Delivered") : L("Cancelada", "Cancelled");

    private static string FormatDriving(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalHours} h {span.Minutes:00} min";
    }

    private void ShowSummary(TripEntry entry, IWin32Window owner)
    {
        using var window = new Form
        {
            Text = L("Resumen del viaje", "Trip summary"), ClientSize = new Size(560, 380),
            StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Foreground
        };
        var content = Grid(1);
        content.Padding = new Padding(20);
        var title = TextLabel(Result(entry), 19, true);
        title.ForeColor = Accent;
        AddRows(content, title, TextLabel($"{entry.Origin}  →  {entry.Destination}", 15, true),
            TextLabel(entry.Cargo, 12), TextLabel($"{AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1")}  ·  {FormatDriving(entry.DrivingSeconds)} {L("al volante", "driving")}", 12),
            TextLabel(entry.Finished?.ToLocalTime().ToString("g") ?? "", 10));
        if (entry.Recording is { } file && Path.GetFileName(file) == file && File.Exists(Path.Combine(LocalData.Recordings, file)))
            AddAction(content, L("Abrir grabación", "Open replay"), () => ShowPlayback(Path.Combine(LocalData.Recordings, file), window));
        AddAction(content, L("Copiar resumen", "Copy summary"), () =>
        {
            try
            {
                Clipboard.SetText($"{entry.Origin} → {entry.Destination} · {entry.Cargo}\n"
                    + $"{AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1")} · {FormatDriving(entry.DrivingSeconds)}");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(window, L("El portapapeles está ocupado.", "The clipboard is busy."), Text);
            }
        });
        AddAction(content, L("Cerrar", "Close"), window.Close);
        window.Controls.Add(content);
        StyleTool(window);
        window.ShowDialog(owner);
    }
}
