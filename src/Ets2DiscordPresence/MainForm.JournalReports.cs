namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private bool MoveTrips(TripEntry[] trips, IWin32Window owner)
    {
        var profiles = new[] { "" }.Concat(preferences.Drivers ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(key, preferences.DriverProfile, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (profiles.Length == 0)
        {
            MessageBox.Show(owner, L("Añade otro conductor en el diario antes de mover el viaje.",
                "Add another driver in the journal before moving the trip."), Text);
            return false;
        }
        using var window = ToolWindow(L("Mover viaje", "Move trip"), new Size(470, 250));
        var content = Grid(1);
        content.Padding = new Padding(16);
        var choice = Choice(content, L("Conductor de destino", "Destination driver"),
            profiles.Select(key => key.Length == 0 ? L("Conductor principal", "Main driver") : key).ToArray(), 0);
        var save = MakeButton(L("Mover", "Move"));
        save.DialogResult = DialogResult.OK;
        AddRows(content, save);
        window.Controls.Add(content);
        StyleTool(window);
        if (window.ShowDialog(owner) != DialogResult.OK) return false;
        var target = new TripJournal(LocalData.JournalPath(profiles[choice.SelectedIndex]));
        if (journal.MoveMany(trips.Select(trip => trip.Id), target)) return true;
        MessageBox.Show(owner, L("No se pudo mover el viaje.", "Could not move the trip."), Text);
        return false;
    }

    private void ShowTripTrash(IWin32Window owner)
    {
        using var window = ToolWindow(L("Papelera de viajes", "Trip trash"), new Size(630, 400));
        var list = new ListBox { Dock = DockStyle.Fill };
        var restore = MakeButton(L("Restaurar viaje", "Restore trip"));
        restore.Dock = DockStyle.Bottom;
        void LoadItems()
        {
            list.Items.Clear();
            list.Items.AddRange(journal.Trash.Where(entry => entry.DeletedAt >= DateTimeOffset.UtcNow.AddDays(-30))
                .OrderByDescending(entry => entry.DeletedAt).Select(entry => new DeletedTrip(entry)).Cast<object>().ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            restore.Enabled = list.SelectedIndex >= 0;
        }
        restore.Click += (_, _) =>
        {
            if (list.SelectedItem is not DeletedTrip selected) return;
            if (journal.Restore(selected.Trip.Id)) LoadItems();
            else MessageBox.Show(window, L("No se pudo restaurar el viaje.", "Could not restore the trip."), Text);
        };
        window.Controls.Add(list);
        window.Controls.Add(restore);
        LoadItems();
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private sealed record DeletedTrip(TripEntry Trip)
    {
        public override string ToString() => $"{Trip.DeletedAt?.ToLocalTime():d} · {Trip.Origin} → {Trip.Destination} · {Trip.Cargo}";
    }

    private void ShowMonthly(IWin32Window owner)
    {
        using var window = ToolWindow(L("Informe mensual", "Monthly report"), new Size(760, 510));
        var header = Grid(1);
        header.Padding = new Padding(12);
        var select = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "MMMM yyyy", ShowUpDown = true,
            AccessibleName = L("Mes", "Month") };
        var overview = TextLabel("", 11, true);
        var report = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false,
            AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        var unit = preferences.Features.Miles ? "mi" : "km";
        report.Columns.Add("truck", L("Camión", "Truck"));
        report.Columns.Add("distance", unit);
        report.Columns.Add("hours", L("Al volante", "Driving"));
        report.Columns.Add("jobs", L("Entregas", "Deliveries"));
        report.Columns.Add("average", "L/100 km");
        report.Columns.Add("fuel", L("Combustible registrado", "Recorded fuel"));
        void RefreshReport()
        {
            var trips = journal.Entries.Where(entry => entry.Finished is { } date
                && date.LocalDateTime.Year == select.Value.Year && date.LocalDateTime.Month == select.Value.Month).ToArray();
            var routes = trips.Where(entry => entry.Result == "delivered").GroupBy(entry => $"{entry.Origin} → {entry.Destination}")
                .OrderByDescending(group => group.Count()).Take(3).Select(group => $"{group.Key} ({group.Count()})");
            overview.Text = $"{AppOptions.Distance(trips.Sum(entry => entry.Km), preferences.Features.Miles)} · "
                + $"{trips.Count(entry => entry.Result == "delivered")} {L("entregas", "deliveries")}\n"
                + $"{L("Rutas frecuentes", "Frequent routes")} · {string.Join(" / ", routes.DefaultIfEmpty("—"))}";
            report.Rows.Clear();
            foreach (var group in trips.GroupBy(entry => entry.Truck ?? L("Camión sin registrar", "Truck not recorded")))
            {
                var fuel = group.Sum(entry => entry.FuelLitres);
                var measured = group.Where(entry => entry.FuelKm >= 1 && entry.FuelLitres > 0 && double.IsFinite(entry.FuelLitres)
                    && double.IsFinite(entry.FuelKm)).ToArray();
                var average = measured.Sum(entry => entry.FuelKm) >= 20
                    ? $"{100 * measured.Sum(entry => entry.FuelLitres) / measured.Sum(entry => entry.FuelKm):N1}" : "—";
                report.Rows.Add(group.Key, AppOptions.Distance(group.Sum(entry => entry.Km), preferences.Features.Miles),
                    FormatDriving(group.Sum(entry => entry.DrivingSeconds)), group.Count(entry => entry.Result == "delivered"),
                    average, fuel > 0 ? $"{fuel:N1} L" : "—");
            }
        }
        select.ValueChanged += (_, _) => RefreshReport();
        AddRows(header, select, overview);
        window.Controls.Add(report);
        window.Controls.Add(header);
        SetWrap(overview, window.ClientSize.Width - 48);
        window.Resize += (_, _) => SetWrap(overview, window.ClientSize.Width - 48);
        RefreshReport();
        StyleTool(window);
        window.ShowDialog(owner);
    }
}
