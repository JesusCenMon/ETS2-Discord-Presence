namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private sealed record DriverOption(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private string? AskDriverName(IWin32Window owner)
    {
        using var window = ToolWindow(L("Nuevo conductor", "New driver"), new Size(440, 160));
        window.MinimumSize = Size.Empty;
        var content = Grid(1);
        content.Padding = new Padding(16);
        var label = L("Nombre para el diario", "Name for the journal");
        var name = new TextBox { Dock = DockStyle.Top, MaxLength = 40, AccessibleName = label };
        var apply = MakeButton(L("Crear conductor", "Create driver"));
        apply.Enabled = false;
        apply.DialogResult = DialogResult.OK;
        name.TextChanged += (_, _) => apply.Enabled = name.Text.Trim().Length > 0;
        window.AcceptButton = apply;
        AddRows(content, TextLabel(label, 12, true), name, apply);
        window.Controls.Add(content);
        StyleTool(window);
        return window.ShowDialog(owner) == DialogResult.OK ? name.Text.Trim() : null;
    }

    private void SwitchDriver(string key, string[] drivers, IWin32Window owner)
    {
        if (string.Equals(key, preferences.DriverProfile, StringComparison.OrdinalIgnoreCase)) return;
        if (liveDelivery)
        {
            MessageBox.Show(owner, L("Termina la entrega activa antes de cambiar de conductor.",
                "Finish the current delivery before switching drivers."), Text);
            return;
        }
        SavePreferences(preferences with { DriverProfile = key, Drivers = drivers });
    }

    private bool EditTripNotes(TripEntry entry, IWin32Window owner)
    {
        using var window = ToolWindow(L("Notas del viaje", "Trip notes"), new Size(540, 330));
        var content = Grid(1);
        content.Padding = new Padding(16);
        var notes = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = 96,
            MaxLength = 4000, Text = entry.Notes, AccessibleName = L("Notas", "Notes")
        };
        var tags = new TextBox { Dock = DockStyle.Top, MaxLength = 240, Text = entry.Tags, AccessibleName = L("Etiquetas", "Tags") };
        var favorite = new CheckBox { AutoSize = true, Checked = entry.Favorite, Text = L("Marcar como favorito", "Mark as favorite") };
        var apply = MakeButton(L("Guardar", "Save"));
        apply.DialogResult = DialogResult.OK;
        window.AcceptButton = apply;
        AddRows(content, TextLabel($"{entry.Origin} → {entry.Destination}", 12, true),
            TextLabel(L("Notas", "Notes"), 9, true), notes,
            TextLabel(L("Etiquetas separadas por comas", "Comma-separated tags"), 9, true), tags, favorite, apply);
        window.Controls.Add(content);
        StyleTool(window);
        if (window.ShowDialog(owner) != DialogResult.OK) return false;
        if (journal.UpdateDetails(entry.Id, notes.Text.Trim(), tags.Text.Trim(), favorite.Checked)) return true;
        MessageBox.Show(owner, L("No se pudo guardar el diario.", "Could not save the journal."), Text);
        return false;
    }

    private string? AskJournalTag(IWin32Window owner)
    {
        using var window = ToolWindow(L("Añadir etiqueta", "Add tag"), new Size(430, 240));
        var content = Grid(1);
        content.Padding = new Padding(16);
        var input = new TextBox { Dock = DockStyle.Top, MaxLength = 50, AccessibleName = L("Etiqueta", "Tag"),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
        input.AutoCompleteCustomSource.AddRange(journal.Entries.SelectMany(entry => JournalQuery.Tags(entry.Tags))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray());
        var apply = MakeButton(L("Aplicar", "Apply"));
        apply.DialogResult = DialogResult.OK;
        window.AcceptButton = apply;
        apply.Enabled = false;
        input.TextChanged += (_, _) => apply.Enabled = !string.IsNullOrWhiteSpace(input.Text) && !input.Text.Contains(',');
        AddRows(content, TextLabel(L("Etiqueta", "Tag"), 11, true), input, apply);
        window.Controls.Add(content);
        StyleTool(window);
        return window.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }

    private static string MostCommon(IEnumerable<string> values) => values.Where(value => value.Length > 0)
        .GroupBy(value => value, StringComparer.CurrentCultureIgnoreCase).OrderByDescending(group => group.Count())
        .FirstOrDefault()?.Key ?? "—";

    private void CompareTrips(TripEntry first, TripEntry second, IWin32Window owner)
    {
        using var window = ToolWindow(L("Comparar viajes", "Compare trips"), new Size(730, 390));
        var grid = Grid(3);
        grid.Padding = new Padding(16);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        void Row(string label, Func<TripEntry, string> value, bool prominent = false)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var labels = new[] { TextLabel(label, 10, true), TextLabel(value(first), prominent ? 13 : 10, prominent),
                TextLabel(value(second), prominent ? 13 : 10, prominent) };
            for (var i = 0; i < labels.Length; i++)
            {
                var cell = labels[i];
                var ratio = i == 0 ? .26 : .37;
                cell.Padding = new Padding(8);
                cell.BackColor = row % 2 == 0 ? shellPalette.Hover : shellPalette.Surface;
                if (i > 0 && prominent) cell.ForeColor = shellPalette.Text;
                grid.Controls.Add(cell, i, row);
                void Wrap() => SetWrap(cell, (int)((window.ClientSize.Width - 56) * ratio));
                window.SizeChanged += (_, _) => Wrap();
                Wrap();
            }
        }
        Row(L("Ruta", "Route"), entry => $"{entry.Origin} → {entry.Destination}", true);
        Row(L("Carga", "Cargo"), entry => entry.Cargo);
        Row(L("Resultado", "Result"), ResultIcon);
        Row(L("Llegada", "Arrival"), entry => entry.Finished?.ToLocalTime().ToString("g") ?? "—");
        Row(L("Distancia", "Distance"), entry => AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1"));
        Row(L("Al volante", "Driving"), entry => FormatDriving(entry.DrivingSeconds));
        Row(L("Velocidad media", "Average speed"), entry => entry.DrivingSeconds > 0 ? SpeedLabel(entry.Km * 3600 / entry.DrivingSeconds) : "—");
        Row(L("Combustible", "Fuel"), entry => entry.FuelLitres > 0 ? $"{entry.FuelLitres:N1} L" : "—");
        Row(L("Paradas", "Stops"), entry => entry.Stops?.ToString() ?? "—");
        Row(L("Etiquetas", "Tags"), entry => string.IsNullOrWhiteSpace(entry.Tags) ? "—" : entry.Tags);
        var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabStop = false };
        var change = first.Km > 0 ? $"{(second.Km / first.Km - 1) * 100:+0.0;-0.0;0} %" : "—";
        var timeChange = first.DrivingSeconds > 0 ? $"{(second.DrivingSeconds / first.DrivingSeconds - 1) * 100:+0.0;-0.0;0} %" : "—";
        var difference = TextLabel(L($"Segundo viaje respecto al primero · Distancia {change} · Tiempo {timeChange}",
            $"Second trip compared with first · Distance {change} · Time {timeChange}"), 10, true);
        difference.Dock = DockStyle.Bottom;
        difference.Padding = new Padding(16, 0, 16, 10);
        scrollPanel.Controls.Add(grid);
        window.Controls.Add(scrollPanel);
        window.Controls.Add(difference);
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private void ShowWeek(IWin32Window owner)
    {
        using var window = ToolWindow(L("Calendario semanal", "Weekly calendar"), new Size(700, 490));
        var root = Grid(1);
        root.Padding = new Padding(16);
        var nav = ToolBar();
        var previous = MakeButton(L("← Anterior", "← Previous"));
        var next = MakeButton(L("Siguiente →", "Next →"));
        var today = MakeButton(L("Esta semana", "This week"));
        nav.Controls.AddRange([previous, today, next]);
        var period = TextLabel("", 16, true);
        var days = new DataGridView
        {
            ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToResizeRows = false,
            Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, TabStop = false
        };
        days.Columns.Add("day", L("Día", "Day"));
        days.Columns.Add("km", preferences.Features.Miles ? "mi" : "km");
        days.Columns.Add("deliveries", L("Entregas", "Deliveries"));
        days.Columns.Add("driving", L("Al volante", "Driving"));
        var week = StartOfWeek(DateTime.Today);
        void Populate()
        {
            period.Text = $"{week:d}  —  {week.AddDays(6):d}";
            days.Rows.Clear();
            for (var i = 0; i < 7; i++)
            {
                var day = week.AddDays(i);
                var entries = journal.Entries.Where(entry => entry.Finished?.LocalDateTime.Date == day).ToArray();
                var culture = System.Globalization.CultureInfo.GetCultureInfo(English ? "en-US" : "es-CO");
                days.Rows.Add(day.ToString("ddd d", culture), $"{entries.Sum(entry => entry.Km) / (preferences.Features.Miles ? 1.609344 : 1):N1}",
                    entries.Count(entry => entry.Result == "delivered"), FormatDriving(entries.Sum(entry => entry.DrivingSeconds)));
            }
        }
        previous.Click += (_, _) => { week = week.AddDays(-7); Populate(); };
        next.Click += (_, _) => { week = week.AddDays(7); Populate(); };
        today.Click += (_, _) => { week = StartOfWeek(DateTime.Today); Populate(); };
        AddRows(root, period, nav);
        window.Controls.Add(days);
        window.Controls.Add(root);
        StyleTool(window);
        Populate();
        window.ShowDialog(owner);
    }

    internal static DateTime StartOfWeek(DateTime date) => date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
}
