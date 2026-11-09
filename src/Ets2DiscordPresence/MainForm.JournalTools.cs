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

    private void ShowDriverStats(IWin32Window owner)
    {
        var entries = journal.Entries;
        var completed = entries.Count(entry => entry.Result == "delivered");
        var distance = entries.Sum(entry => entry.Km);
        var completion = entries.Count > 0 ? $"{(double)completed / entries.Count:P0}" : "—";
        var topRoute = MostCommon(entries.Select(entry => $"{entry.Origin} → {entry.Destination}"));
        var topTruck = MostCommon(entries.Select(entry => entry.Truck).OfType<string>());
        using var window = ToolWindow(L("Estadísticas del conductor", "Driver statistics"), new Size(520, 390));
        var content = Grid(1);
        content.Padding = new Padding(16);
        AddRows(content,
            TextLabel($"{L("Entregas completadas", "Completed deliveries")} · {completed}/{entries.Count} ({completion})", 13, true),
            TextLabel($"{L("Distancia", "Distance")} · {AppOptions.Distance(distance, preferences.Features.Miles, "N1")}", 11),
            TextLabel($"{L("Tiempo al volante", "Driving time")} · {FormatDriving(entries.Sum(entry => entry.DrivingSeconds))}", 11),
            TextLabel($"{L("Distancia media", "Average distance")} · {(entries.Count > 0 ? AppOptions.Distance(distance / entries.Count, preferences.Features.Miles, "N1") : "—")}", 11),
            TextLabel($"{L("Ruta más frecuente", "Most frequent route")} · {topRoute}", 11),
            TextLabel($"{L("Camión más usado", "Most used truck")} · {topTruck}", 11));
        window.Controls.Add(content);
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private static string MostCommon(IEnumerable<string> values) => values.Where(value => value.Length > 0)
        .GroupBy(value => value, StringComparer.CurrentCultureIgnoreCase).OrderByDescending(group => group.Count())
        .FirstOrDefault()?.Key ?? "—";

    private void CompareTrips(TripEntry first, TripEntry second, IWin32Window owner)
    {
        using var window = ToolWindow(L("Comparar entregas", "Compare deliveries"), new Size(730, 390));
        var grid = Grid(2);
        grid.Padding = new Padding(16);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var tripNumber = 0;
        foreach (var entry in new[] { first, second })
        {
            var card = Grid(1);
            card.BackColor = Surface;
            card.Padding = new Padding(12);
            card.Margin = new Padding(4);
            var route = TextLabel($"{entry.Origin} → {entry.Destination}", 14, true);
            var load = TextLabel(entry.Cargo, 11);
            var tags = TextLabel(entry.Tags, 10);
            var speed = entry.DrivingSeconds > 0 ? AppOptions.Distance(entry.Km * 3600 / entry.DrivingSeconds,
                preferences.Features.Miles, "N1") + "/h" : "—";
            var fuel = entry.FuelLitres > 0 ? $"{entry.FuelLitres:N1} L" : "—";
            void Wrap()
            {
                foreach (var label in new[] { route, load, tags }) SetWrap(label, grid.ClientSize.Width / 2 - 54);
            }
            grid.SizeChanged += (_, _) => Wrap();
            AddRows(card, TextLabel($"{L("Viaje", "Trip")} {tripNumber + 1}", 9, true), route, load,
                TextLabel(Result(entry), 10), TextLabel(entry.Finished?.ToLocalTime().ToString("g") ?? "", 10),
                TextLabel(AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1"), 16, true), TextLabel(FormatDriving(entry.DrivingSeconds), 12, true),
                TextLabel($"{L("Velocidad media", "Average speed")} · {speed}", 10),
                TextLabel($"{L("Combustible", "Fuel")} · {fuel}", 10),
                TextLabel($"{L("Paradas detectadas", "Stops detected")} · {entry.Stops?.ToString() ?? "—"}", 10), tags);
            grid.Controls.Add(card, tripNumber++, 0);
        }
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
