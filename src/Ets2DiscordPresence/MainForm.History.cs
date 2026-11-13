namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private Guid? lastJournalTrip;
    private int lastJournalScroll;

    private void ShowHistory()
    {
        using var window = ToolWindow(L("Diario de entregas", "Delivery journal"), new Size(980, 590), placement: "journal");
        var header = Grid(1);
        header.Padding = new Padding(14, 12, 14, 6);
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        var cityFilter = new TextBox { Width = 240, PlaceholderText = L("Ciudad, carga o etiqueta", "City, cargo or tag"), AccessibleName = L("Buscar en el diario", "Search the journal") };
        var from = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 152, AccessibleName = L("Desde", "From") };
        var to = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 152, AccessibleName = L("Hasta", "To") };
        var export = MakeButton(L("Exportar CSV", "Export CSV"));
        var favorites = new CheckBox { Text = L("Solo favoritos", "Favorites only"), AutoSize = true };
        var tools = ToolBar();
        var driver = new ComboBox { Width = 224, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = L("Conductor", "Driver") };
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
        filters.Controls.AddRange([new Label { Text = L("Desde", "From"), AutoSize = true }, from,
            new Label { Text = L("Hasta", "To"), AutoSize = true }, to, favorites]);
        ComboBox Filter(string es, string en, params string[] items)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 208, AccessibleName = L(es, en) };
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
        var activeFilters = ToolBar();
        var resultCount = TextLabel("", 9, true);
        bool refreshing = false;
        var secondary = new ContextMenuStrip();
        var more = MakeButton("⋯");
        more.AccessibleName = L("Más acciones del diario", "More journal actions");
        foreach (var action in new[] { addDriver, calendar, notes, remove, move, trash, favorite, tag })
        {
            tools.Controls.Remove(action);
            var item = secondary.Items.Add(action.Text, null, (_, _) => action.PerformClick());
            secondary.Opening += (_, _) => item.Enabled = action.Enabled;
        }
        more.Click += (_, _) => secondary.Show(more, new Point(0, more.Height));
        window.Disposed += (_, _) =>
        {
            secondary.Dispose();
            foreach (var action in new[] { addDriver, calendar, notes, remove, move, trash, favorite, tag }) action.Dispose();
        };
        tools.Controls.Add(more);
        var searchBar = ToolBar();
        var toggleFilters = MakeButton(L("Filtros", "Filters"));
        searchBar.Controls.AddRange([TextLabel(L("Buscar", "Search"), 10, true), cityFilter, toggleFilters, export]);
        filters.Visible = false;
        toggleFilters.Click += (_, _) =>
        {
            filters.Visible = !filters.Visible;
            toggleFilters.Text = filters.Visible ? L("Ocultar filtros", "Hide filters") : L("Filtros", "Filters");
            if (filters.Visible) from.Focus();
        };
        var title = ToolBar();
        title.Controls.AddRange([TextLabel(L("Diario", "Journal"), 23, true), resultCount]);
        AddRows(header, title, tools, searchBar, filters, activeFilters, stats);

        var history = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Surface, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        history.EnableHeadersVisualStyles = false;
        history.ColumnHeadersDefaultCellStyle.BackColor = shellPalette.Hover;
        history.ColumnHeadersDefaultCellStyle.ForeColor = shellPalette.Text;
        history.ColumnHeadersDefaultCellStyle.SelectionBackColor = shellPalette.Hover;
        history.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        history.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        history.DefaultCellStyle.BackColor = shellPalette.Surface;
        history.DefaultCellStyle.ForeColor = Foreground;
        history.DefaultCellStyle.SelectionBackColor = shellPalette.Hover;
        history.DefaultCellStyle.SelectionForeColor = shellPalette.Text;
        history.GridColor = shellPalette.Border;
        history.AlternatingRowsDefaultCellStyle.BackColor = SystemInformation.HighContrast
            ? shellPalette.Surface : Color.FromArgb((shellPalette.Surface.R * 3 + shellPalette.Hover.R) / 4,
                (shellPalette.Surface.G * 3 + shellPalette.Hover.G) / 4, (shellPalette.Surface.B * 3 + shellPalette.Hover.B) / 4);
        history.RowHeadersVisible = true;
        history.RowHeadersWidth = 30;
        history.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        history.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        history.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
        foreach (var (key, label, size) in new[]
        {
            ("date", L("Fecha", "Date"), 100), ("origin", L("Origen", "Origin"), 90),
            ("destination", L("Destino", "Destination"), 90), ("cargo", L("Carga / Viaje", "Cargo / Trip"), 145),
            ("result", L("Resultado", "Result"), 86), ("km", preferences.Features.Miles ? "mi" : "km", 55), ("time", L("Al volante · h/min", "Driving · h/min"), 75), ("revenue", L("Ingresos", "Revenue"), 75)
        })
        {
            var column = new DataGridViewTextBoxColumn { Name = key, HeaderText = label, FillWeight = size, SortMode = DataGridViewColumnSortMode.Programmatic };
            if (key is "km" or "time" or "revenue")
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            history.Columns.Add(column);
        }
        var sortColumn = "date";
        history.RowPostPaint += (_, e) =>
        {
            var bounds = Rectangle.Intersect(e.RowBounds, history.ClientRectangle);
            if (bounds.Width < 4 || bounds.Height < 4) return;
            var current = history.Rows[e.RowIndex];
            if (current.Tag is TripEntry entry)
            {
                if (entry.Favorite)
                    TextRenderer.DrawText(e.Graphics, "★", history.Font,
                        new Rectangle(bounds.X, bounds.Y, history.RowHeadersWidth, bounds.Height), shellPalette.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (sortColumn == "date" && e.RowIndex > 0 && history.Rows[e.RowIndex - 1].Tag is TripEntry previous
                    && previous.Finished?.LocalDateTime.Date != entry.Finished?.LocalDateTime.Date)
                {
                    using var divider = new Pen(shellPalette.Border, 2);
                    e.Graphics.DrawLine(divider, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
                }
            }
            if (history.Focused && history.CurrentCell?.RowIndex == e.RowIndex)
            {
                using var stroke = new Pen(shellPalette.Accent);
                e.Graphics.DrawRectangle(stroke, Rectangle.Inflate(bounds, -1, -1));
            }
        };
        var entries = Array.Empty<TripEntry>();
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
            if (refreshing) return;
            refreshing = true;
            try
            {
                var selectedIds = Selection().Select(entry => entry.Id).ToHashSet();
                var currentId = (history.CurrentRow?.Tag as TripEntry)?.Id ?? lastJournalTrip;
                var firstVisible = Math.Max(history.FirstDisplayedScrollingRowIndex, history.RowCount == 0 ? lastJournalScroll : 0);
                RefreshFilterOptions();
                var search = cityFilter.Text.Trim();
                activeFilters.SuspendLayout();
                foreach (var old in activeFilters.Controls.Cast<Control>().ToArray()) old.Dispose();
                void Chip(string text, Action removeFilter)
                {
                    var button = MakeButton(text + "  ×");
                    button.AccessibleName = L($"Quitar filtro {text}", $"Remove filter {text}");
                    button.Click += (_, _) => { removeFilter(); RefreshHistory(); cityFilter.Focus(); };
                    activeFilters.Controls.Add(button);
                }
                if (search.Length > 0) Chip(CompactLine(search), cityFilter.Clear);
                if (favorites.Checked) Chip(favorites.Text, () => favorites.Checked = false);
                if (from.Checked) Chip($"{L("Desde", "From")} {from.Value:d}", () => from.Checked = false);
                if (to.Checked) Chip($"{L("Hasta", "To")} {to.Value:d}", () => to.Checked = false);
                foreach (var filter in new[] { resultFilter, truckFilter, distanceFilter, tagFilter })
                    if (filter.SelectedIndex > 0) Chip(CompactLine(filter.Text), () => filter.SelectedIndex = 0);
                activeFilters.Visible = activeFilters.Controls.Count > 0;
                activeFilters.ResumeLayout();
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
                    var row = history.Rows.Add(entry.Finished is { } end ? $"{end.ToLocalTime():d}\n{end.ToLocalTime():t}" : "—",
                        entry.Origin, entry.Destination, JourneyLabel(entry), ResultIcon(entry), $"{(preferences.Features.Miles ? entry.Km / 1.609344 : entry.Km):N1}", FormatDriving(entry.DrivingSeconds), entry.Revenue?.ToString("N0") ?? "—");
                    history.Rows[row].Tag = entry;
                }
                if (entries.Length > 0)
                {
                    var index = Array.FindIndex(entries, entry => entry.Id == currentId);
                    history.CurrentCell = history.Rows[Math.Max(0, index)].Cells["destination"];
                    history.ClearSelection();
                    foreach (DataGridViewRow row in history.Rows)
                        if (row.Tag is TripEntry entry && selectedIds.Contains(entry.Id)) row.Selected = true;
                    if (firstVisible >= 0) history.FirstDisplayedScrollingRowIndex = Math.Min(firstVisible, entries.Length - 1);
                }
                resultCount.Text = L($"{entries.Length:N0} viajes", $"{entries.Length:N0} trips");
                var completed = entries.Where(entry => entry.Result == "delivered").ToArray();
                var topRoute = completed.GroupBy(entry => $"{entry.Origin} → {entry.Destination}")
                    .OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
                stats.Text = entries.Length == 0
                    ? journal.Entries.Count == 0 ? L("Diario vacío · Aquí aparecerán tus entregas terminadas.", "Journal empty · Completed deliveries will appear here.")
                        : L("Sin resultados para estos filtros.", "No results for these filters.")
                    : $"{completed.Length} {L("entregadas", "delivered")}  ·  {entries.Count(entry => entry.Result == "cancelled")} {L("canceladas", "cancelled")}  ·  "
                        + $"{AppOptions.Distance(entries.Sum(entry => entry.Km), preferences.Features.Miles, "N1")}  ·  {FormatDriving(entries.Sum(entry => entry.DrivingSeconds))} {L("al volante", "driving")}"
                        + (topRoute is null ? "" : $"\n{L("Ruta más frecuente", "Most frequent route")} · {topRoute}");
                export.Enabled = entries.Length > 0;
            }
            finally { refreshing = false; }
            UpdateActions();
        }
        TripEntry[] Selection() => history.SelectedRows.Cast<DataGridViewRow>().OrderBy(row => row.Index)
            .Select(row => row.Tag).OfType<TripEntry>().ToArray();
        void UpdateActions()
        {
            if (refreshing) return;
            var count = Selection().Length;
            compare.Text = L($"Comparar ({count}/2)", $"Compare ({count}/2)");
            notes.Enabled = remove.Enabled = count == 1;
            move.Enabled = favorite.Enabled = tag.Enabled = count > 0;
            compare.Enabled = count == 2;
        }
        history.SelectionChanged += (_, _) => UpdateActions();
        history.GotFocus += (_, _) => history.Invalidate();
        history.LostFocus += (_, _) => history.Invalidate();
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
        totals.Click += (_, _) => ShowTravelStatistics(window);
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
        using var searchDelay = new System.Windows.Forms.Timer { Interval = 220 };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); RefreshHistory(); };
        cityFilter.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        from.ValueChanged += (_, _) => RefreshHistory();
        to.ValueChanged += (_, _) => RefreshHistory();
        history.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && history.Rows[e.RowIndex].Tag is TripEntry entry) ShowSummary(entry, window);
        };
        history.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && Selection() is [var entry]) ShowSummary(entry, window);
            else if (e.KeyCode == Keys.Delete) remove.PerformClick();
            else if (e.Control && e.KeyCode == Keys.A) history.SelectAll();
            else return;
            e.SuppressKeyPress = true;
        };
        using var context = new ContextMenuStrip();
        context.Items.Add(L("Abrir resumen", "Open summary"), null, (_, _) =>
        {
            if (Selection() is [var entry]) ShowSummary(entry, window);
        });
        var menuActions = new[] { notes, favorite, tag, move, compare, remove };
        foreach (var button in menuActions) context.Items.Add(button.Text, null, (_, _) => button.PerformClick());
        context.Opening += (_, _) =>
        {
            context.Items[0].Enabled = Selection().Length == 1;
            for (var index = 0; index < menuActions.Length; index++)
                context.Items[index + 1].Enabled = menuActions[index].Enabled;
        };
        history.ContextMenuStrip = context;
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
        lastJournalTrip = (history.CurrentRow?.Tag as TripEntry)?.Id;
        lastJournalScroll = Math.Max(0, history.FirstDisplayedScrollingRowIndex);
    }

    private string JourneyLabel(TripEntry entry) => entry.JourneyType switch
    {
        "coach" => L("Pasajeros", "Passengers"),
        "free" => L("Conducción libre", "Free driving"),
        _ => entry.Cargo
    };

    private string Result(TripEntry entry) => entry.Result == "delivered"
        ? L("Entregada", "Delivered") : entry.Result == "ended" ? L("Sesión cerrada", "Session ended") : L("Cancelada", "Cancelled");

    private string ResultIcon(TripEntry entry)
    {
        var label = (entry.Result switch { "delivered" => "✓  ", "ended" => "■  ", _ => "×  " }) + Result(entry);
        return entry.Measurements?.Quality == "recovered" ? label + L(" · ↺ Recuperado", " · ↺ Recovered") : label;
    }

    private static string FormatDriving(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalHours} h {span.Minutes:00} min";
    }

    private void ShowSummary(TripEntry entry, IWin32Window owner)
    {
        using var window = ToolWindow(entry.JourneyType is null ? L("Detalles de la entrega", "Delivery details") : L("Detalles del viaje", "Trip details"), new Size(560, 470));
        var content = Grid(1);
        content.Padding = new Padding(20);
        var title = TextLabel(entry.JourneyType == "free" ? JourneyLabel(entry) : $"{entry.Origin}  →  {entry.Destination}", 19, true);
        title.ForeColor = Accent;
        void Section(string es, string en)
        {
            var heading = TextLabel(L(es, en), 12, true);
            heading.Margin = new Padding(0, 12, 0, 4);
            AddRows(content, heading);
        }
        AddRows(content, TextLabel(ResultIcon(entry), 11, true), TextLabel(JourneyLabel(entry), 12), title);
        if (entry.Revenue is { } revenue) AddRows(content, TextLabel(L($"Ingresos · {revenue:N0}", $"Revenue · {revenue:N0}"), 10));
        void WrapSummary()
        {
            var width = window.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            foreach (var label in content.Controls.OfType<Label>()) SetWrap(label, width);
        }
        window.ClientSizeChanged += (_, _) => WrapSummary();
        window.Shown += (_, _) => WrapSummary();
        Section("Tiempos y distancia", "Time and distance");
        AddRows(content, TextLabel($"{AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1")}  ·  {FormatDriving(entry.DrivingSeconds)} {L("al volante", "driving")}", 12),
            TextLabel($"{L("Salida", "Departure")} · {entry.Started.ToLocalTime():g}", 10),
            TextLabel($"{L("Llegada", "Arrival")} · {entry.Finished?.ToLocalTime():g}", 10));
        if (!string.IsNullOrWhiteSpace(entry.Truck))
            AddRows(content, TextLabel($"{L("Camión", "Truck")} · {entry.Truck}", 10));
        if (entry.Measurements is { } measured)
        {
            var quality = measured.Quality switch
            {
                "complete" => L("Completo", "Complete"),
                "recovered" => L("Recuperado", "Recovered"),
                _ => L("Parcial", "Partial")
            };
            AddRows(content, TextLabel(L($"Calidad de datos · {quality}", $"Data quality · {quality}"), 10, true));
            if (measured.Quality != "complete")
                AddRows(content, TextLabel(L($"Seguimiento desde {measured.TrackedFrom.LocalDateTime:g}", $"Tracking since {measured.TrackedFrom.LocalDateTime:g}"), 10));
            if (entry.Finished is { } finished && measured.PickedUpAt is { } pickedUp)
                AddRows(content, TextLabel(L($"Tiempo de viaje observado · {Duration((finished - pickedUp).TotalSeconds)}",
                    $"Observed trip time · {Duration((finished - pickedUp).TotalSeconds)}"), 10));
            if (measured.MaximumKph is { } max)
                AddRows(content, TextLabel(L($"Al volante {Duration(measured.MovingSeconds)} · Detenido {Duration(measured.StoppedSeconds)} · Máxima {SpeedLabel(max)}",
                    $"Driving {Duration(measured.MovingSeconds)} · Stopped {Duration(measured.StoppedSeconds)} · Top speed {SpeedLabel(max)}"), 10));
            if (measured.AverageKph is { } avg)
                AddRows(content, TextLabel(L($"Velocidad media · {SpeedLabel(avg)}", $"Average speed · {SpeedLabel(avg)}"), 10));
            AddRows(content, TextLabel(L($"Distancia observada · {AppOptions.Distance(measured.ObservedKm, preferences.Features.Miles)}",
                $"Observed distance · {AppOptions.Distance(measured.ObservedKm, preferences.Features.Miles)}"), 10));
            if (measured.DeliveredKm is { } delivered)
                AddRows(content, TextLabel(L($"Distancia según ETS2 · {AppOptions.Distance(delivered, preferences.Features.Miles)}",
                    $"Distance from ETS2 · {AppOptions.Distance(delivered, preferences.Features.Miles)}"), 10));
            if (measured.Countries is { Length: > 1 } countries)
                AddRows(content, TextLabel(string.Join(" → ", countries.Select(code => Countries.Name(code, English) ?? code)), 10));
            if (entry.FuelLitres > 0 || measured.RefueledLitres > 0) Section("Combustible", "Fuel");
            if (entry.FuelKm >= 20 && entry.FuelKm >= measured.ObservedKm * .8 && entry.FuelLitres > 0)
                AddRows(content, TextLabel(L($"Consumo medio · {100 * entry.FuelLitres / entry.FuelKm:N1} L/100 km",
                    $"Average fuel use · {100 * entry.FuelLitres / entry.FuelKm:N1} L/100 km"), 10));
            if (entry.FuelLitres > 0 || measured.RefueledLitres > 0)
                AddRows(content, TextLabel(L($"Consumidos {entry.FuelLitres:N1} L · Repostados {measured.RefueledLitres:N1} L",
                    $"Used {entry.FuelLitres:N1} L · Refueled {measured.RefueledLitres:N1} L"), 10));
        }
        else if (entry.FuelLitres > 0)
        {
            Section("Combustible", "Fuel");
            AddRows(content, TextLabel($"{L("Combustible registrado", "Recorded fuel")} · {entry.FuelLitres:N1} L", 10));
        }
        if (entry.OnTime is { } onTime)
            AddRows(content, TextLabel(onTime ? L("Entregada a tiempo", "Delivered on time") : L("Entregada fuera de plazo", "Delivered late"), 10));
        if (!string.IsNullOrWhiteSpace(entry.Notes) || !string.IsNullOrWhiteSpace(entry.Tags))
        {
            Section("Notas", "Notes");
            if (!string.IsNullOrWhiteSpace(entry.Notes))
            {
                var notes = TextLabel(entry.Notes, 10);
                AddRows(content, notes);
                SetWrap(notes, window.ClientSize.Width - 60);
                window.Resize += (_, _) => SetWrap(notes, window.ClientSize.Width - 60);
            }
            if (!string.IsNullOrWhiteSpace(entry.Tags)) AddRows(content, TextLabel(entry.Tags, 9));
        }
        if (LocalData.FindRecording(entry.Recording) is { } replay)
        {
            AddAction(content, L("Ver gráfico de velocidad", "Show speed chart"), () =>
            {
                if (content.Controls.OfType<SpeedChart>().Any()) return;
                DataAction(window, () => AddRows(content, new SpeedChart(TelemetryReplay.Read(replay), Surface, Foreground, Accent)
                    { Dock = DockStyle.Top, Height = 96, AccessibleName = L("Velocidad del viaje", "Trip speed") }));
            });
            AddAction(content, L("Ver recorrido", "View route"), () => ShowPlayback(replay, window));
        }
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
        var footer = ToolBar();
        footer.Dock = DockStyle.Bottom;
        var close = MakeButton(L("Cerrar", "Close"));
        close.Click += (_, _) => window.Close();
        footer.Controls.Add(close);
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        viewport.Controls.Add(content);
        window.Controls.Add(viewport);
        window.Controls.Add(footer);
        StyleTool(window);
        window.ShowDialog(owner);
    }
}
