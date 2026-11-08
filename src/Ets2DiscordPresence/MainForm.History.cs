namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ShowHistory()
    {
        using var window = new Form
        {
            Text = L("Diario de entregas", "Delivery journal"), ClientSize = new Size(950, 565),
            MinimumSize = new Size(550, 390), StartPosition = FormStartPosition.CenterParent,
            BackColor = Themed(Background), ForeColor = Themed(Foreground)
        };
        var header = Grid(1);
        header.Padding = new Padding(14, 12, 14, 6);
        var filters = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        var cityFilter = new TextBox { Width = 160, PlaceholderText = L("Filtrar por ciudad", "Filter by city"), AccessibleName = L("Ciudad", "City") };
        var from = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 123, AccessibleName = L("Desde", "From") };
        var to = new DateTimePicker { ShowCheckBox = true, Checked = false, Format = DateTimePickerFormat.Short, Width = 123, AccessibleName = L("Hasta", "To") };
        var export = MakeButton(L("Exportar CSV", "Export CSV"));
        filters.Controls.AddRange([cityFilter, new Label { Text = L("Desde", "From"), AutoSize = true }, from,
            new Label { Text = L("Hasta", "To"), AutoSize = true }, to, export]);
        var stats = TextLabel("", 11, true);
        AddRows(header, TextLabel(L("Tus viajes registrados", "Your recorded trips"), 18, true), stats, filters);

        var history = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Themed(Surface), BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        history.DefaultCellStyle.BackColor = Themed(Surface);
        history.DefaultCellStyle.ForeColor = Themed(Foreground);
        history.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        history.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        foreach (var (key, label, size) in new[]
        {
            ("date", L("Fecha", "Date"), 100), ("origin", L("Origen", "Origin"), 90),
            ("destination", L("Destino", "Destination"), 90), ("cargo", L("Carga", "Cargo"), 145),
            ("result", L("Resultado", "Result"), 86), ("km", "km", 55), ("time", L("Al volante", "Driving"), 75)
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
                && (entry.Origin.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || entry.Destination.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
                .OrderByDescending(entry => entry.Finished).ToArray();
            history.Rows.Clear();
            foreach (var entry in entries)
                history.Rows.Add(entry.Finished?.ToLocalTime().ToString("g") ?? "", entry.Origin, entry.Destination,
                    entry.Cargo, Result(entry), $"{entry.Km:N1}", FormatDriving(entry.DrivingSeconds));
            var completed = entries.Where(entry => entry.Result == "delivered").ToArray();
            var topRoute = completed.GroupBy(entry => $"{entry.Origin} → {entry.Destination}")
                .OrderByDescending(group => group.Count()).FirstOrDefault()?.Key;
            stats.Text = entries.Length == 0 ? L("Todavía no hay viajes para este filtro.", "No trips match this filter yet.")
                : $"{completed.Length} {L("entregadas", "delivered")}  ·  {entries.Length - completed.Length} {L("canceladas", "cancelled")}  ·  "
                    + $"{entries.Sum(entry => entry.Km):N1} km  ·  {FormatDriving(entries.Sum(entry => entry.DrivingSeconds))} {L("al volante", "driving")}"
                    + (topRoute is null ? "" : $"\n{L("Ruta más frecuente", "Most frequent route")} · {topRoute}");
            export.Enabled = entries.Length > 0;
        }
        cityFilter.TextChanged += (_, _) => RefreshHistory();
        from.ValueChanged += (_, _) => RefreshHistory();
        to.ValueChanged += (_, _) => RefreshHistory();
        history.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < entries.Length) ShowSummary(entries[e.RowIndex], window);
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
        Restyle(window);
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
            Text = L("Resumen del viaje", "Trip summary"), ClientSize = new Size(520, 290),
            StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = Foreground
        };
        var content = Grid(1);
        content.Padding = new Padding(20);
        var title = TextLabel(Result(entry), 19, true);
        title.ForeColor = Mint;
        AddRows(content, title, TextLabel($"{entry.Origin}  →  {entry.Destination}", 15, true),
            TextLabel(entry.Cargo, 12), TextLabel($"{entry.Km:N1} km  ·  {FormatDriving(entry.DrivingSeconds)} {L("al volante", "driving")}", 12),
            TextLabel(entry.Finished?.ToLocalTime().ToString("g") ?? "", 10));
        AddAction(content, L("Cerrar", "Close"), window.Close);
        window.Controls.Add(content);
        Restyle(window);
        window.ShowDialog(owner);
    }
}
