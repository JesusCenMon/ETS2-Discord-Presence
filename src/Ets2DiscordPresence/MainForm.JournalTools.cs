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
            void Wrap()
            {
                foreach (var label in new[] { route, load, tags }) SetWrap(label, grid.ClientSize.Width / 2 - 54);
            }
            grid.SizeChanged += (_, _) => Wrap();
            AddRows(card, TextLabel($"{L("Viaje", "Trip")} {tripNumber + 1}", 9, true), route, load,
                TextLabel(Result(entry), 10), TextLabel(entry.Finished?.ToLocalTime().ToString("g") ?? "", 10),
                TextLabel(AppOptions.Distance(entry.Km, preferences.Features.Miles, "N1"), 16, true), TextLabel(FormatDriving(entry.DrivingSeconds), 12, true), tags);
            grid.Controls.Add(card, tripNumber++, 0);
        }
        var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabStop = false };
        scrollPanel.Controls.Add(grid);
        window.Controls.Add(scrollPanel);
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
