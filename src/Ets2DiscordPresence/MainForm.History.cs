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
        var favorite = MakeButton(L("Marcar favoritos", "Mark favorites"));
        var tag = MakeButton(L("Añadir etiqueta", "Add tag"));
        var totals = MakeButton(L("Estadísticas", "Statistics"));
        var report = MakeButton(L("Informe mensual", "Monthly report"));
        var move = MakeButton(L("Mover viaje", "Move trip"));
        var trash = MakeButton(L("Papelera", "Trash"));
        tools.Controls.AddRange([driver, addDriver, calendar, compare, notes, remove, report, move, trash, favorite, tag, totals]);
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
        ComboBox Filter(string es, string en, params string[] items)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 136, AccessibleName = L(es, en) };
            box.Items.AddRange(items);
            box.SelectedIndex = 0;
            filters.Controls.Add(box);
            return box;
        }
        var resultFilter = Filter("Resultado", "Result", L("Todos los resultados", "All results"), L("Entregadas", "Delivered"), L("Canceladas", "Cancelled"));
        var truckFilter = Filter("Camión", "Truck", L("Todos los camiones", "All trucks"));
        var unit = preferences.Features.Miles ? "mi" : "km";
        var distanceFilter = Filter("Distancia", "Distance", L("Toda distancia", "All distances"), $"<100 {unit}",
            $"100–499 {unit}", $"500–999 {unit}", $"1000+ {unit}");
        var tagFilter = Filter("Etiqueta", "Tag", L("Todas las etiquetas", "All tags"));
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
            var column = new DataGridViewTextBoxColumn { Name = key, HeaderText = label, FillWeight = size, SortMode = DataGridViewColumnSortMode.Programmatic };
            history.Columns.Add(column);
        }
        var entries = Array.Empty<TripEntry>();
        var sortColumn = "date";
        var descending = true;
        void RefreshFilterOptions()
        {
            SetOptions(truckFilter, journal.Entries.Select(entry => entry.Truck).OfType<string>());
            SetOptions(tagFilter, journal.Entries.SelectMany(entry => JournalQuery.Tags(entry.Tags)));
            void SetOptions(ComboBox box, IEnumerable<string> values)
            {
                var selected = box.SelectedItem;
                while (box.Items.Count > 1) box.Items.RemoveAt(1);
                box.Items.AddRange(values.Where(value => value.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).Cast<object>().ToArray());
                box.SelectedIndex = selected is not null && box.Items.Contains(selected) ? box.Items.IndexOf(selected) : 0;
            }
        }
        void RefreshHistory()
        {
            RefreshFilterOptions();
            var search = cityFilter.Text.Trim();
            var matches = journal.Entries.Where(entry => entry.Finished is { } finish
                && (!from.Checked || finish.LocalDateTime.Date >= from.Value.Date)
                && (!to.Checked || finish.LocalDateTime.Date <= to.Value.Date)
                && (!favorites.Checked || entry.Favorite)
                && new[] { entry.Origin, entry.Destination, entry.Cargo, entry.Tags, entry.Notes }
                    .Any(value => value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true)
                && JournalQuery.Matches(entry, resultFilter.SelectedIndex, truckFilter.SelectedIndex > 0 ? truckFilter.Text : null,
                    distanceFilter.SelectedIndex, preferences.Features.Miles, tagFilter.SelectedIndex > 0 ? tagFilter.Text : null));
            entries = JournalQuery.Sort(matches, sortColumn, descending).ToArray();
            foreach (DataGridViewColumn column in history.Columns)
                column.HeaderCell.SortGlyphDirection = column.Name != sortColumn ? SortOrder.None
                    : descending ? SortOrder.Descending : SortOrder.Ascending;
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
            notes.Enabled = remove.Enabled = count == 1;
            move.Enabled = favorite.Enabled = tag.Enabled = count > 0;
            compare.Enabled = count == 2;
        }
        history.SelectionChanged += (_, _) => UpdateActions();
        history.ColumnHeaderMouseClick += (_, e) =>
        {
            var key = history.Columns[e.ColumnIndex].Name;
            descending = key == sortColumn ? !descending : key == "date";
            sortColumn = key;
            RefreshHistory();
        };
        foreach (var filter in new[] { resultFilter, truckFilter, distanceFilter, tagFilter })
            filter.SelectionChangeCommitted += (_, _) => RefreshHistory();
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
            var selected = Selection();
            if (selected.Length > 0 && MoveTrips(selected, window)) RefreshHistory();
        };
        favorite.Click += (_, _) => EditSelected(null, true);
        tag.Click += (_, _) =>
        {
            if (AskJournalTag(window) is { } value) EditSelected(value, false);
        };
        totals.Click += (_, _) => ShowDriverStats(window);
        void EditSelected(string? value, bool mark)
        {
            var ids = Selection().Select(entry => entry.Id).ToArray();
            if (ids.Length == 0) return;
            if (!journal.EditMany(ids, value, mark))
                MessageBox.Show(window, L("No se pudo guardar el diario.", "Could not save the journal."), Text);
            RefreshHistory();
        }
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
            Text = L("Resumen del viaje", "Trip summary"), ClientSize = new Size(560, 470),
            StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Foreground
        };
        var content = Grid(1);
        content.Padding = new Padding(20);
        var title = TextLabel(Result(entry), 19, true);
        title.ForeColor = Accent;
        AddRows(content, title, TextLabel($"{entry.Origin}  →  {entry.Destination}", 15, true),
            TextLabel(entry.Cargo, 12), TextLabel($"{AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1")}  ·  {FormatDriving(entry.DrivingSeconds)} {L("al volante", "driving")}", 12),
            TextLabel($"{L("Salida", "Departure")} · {entry.Started.ToLocalTime():g}", 10),
            TextLabel($"{L("Llegada", "Arrival")} · {entry.Finished?.ToLocalTime():g}", 10));
        if (!string.IsNullOrWhiteSpace(entry.Truck))
            AddRows(content, TextLabel($"{L("Camión", "Truck")} · {entry.Truck}", 10));
        if (entry.FuelLitres > 0)
            AddRows(content, TextLabel($"{L("Combustible registrado", "Recorded fuel")} · {entry.FuelLitres:N1} L", 10));
        if (entry.OnTime is { } onTime)
            AddRows(content, TextLabel(onTime ? L("Entregada a tiempo", "Delivered on time") : L("Entregada fuera de plazo", "Delivered late"), 10));
        if (LocalData.FindRecording(entry.Recording) is { } replay)
            AddAction(content, L("Ver recorrido", "View route"), () => ShowPlayback(replay, window));
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
