namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ShowTravelStatistics(IWin32Window owner)
    {
        using var window = ToolWindow(L("Estadísticas del conductor", "Driver statistics"), new Size(810, 520));
        var header = Grid(1);
        header.Padding = new Padding(12);
        var period = Choice(header, L("Período", "Period"),
            [L("7 días", "7 days"), L("30 días", "30 days"), L("Año", "Year"), L("Todo", "All")], 3);
        var view = Choice(header, L("Vista", "View"),
            [L("Resumen", "Overview"), L("Países", "Countries"), L("Ciudades", "Cities"), L("Rutas", "Routes"),
             L("Camiones", "Trucks"), L("Récords", "Records")], 0);
        var goal = MakeButton(L("Meta mensual…", "Monthly goal…"));
        var charts = MakeButton(L("Calendario anual y gráfico mensual…", "Annual calendar and monthly chart…"));
        var actions = ToolBar();
        actions.Controls.AddRange([goal, charts]);
        AddRows(header, actions);
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        string Km(double distance) => AppOptions.Distance(distance, preferences.Features.Miles, "N1");
        static string Route(TripEntry trip) => $"{trip.Origin} → {trip.Destination}";
        static int Deliveries(IEnumerable<TripEntry> trips) => trips.Count(trip => trip.Result == "delivered");
        void RefreshStatistics()
        {
            var today = DateTime.Today;
            var start = period.SelectedIndex switch { 0 => today.AddDays(-6), 1 => today.AddDays(-29), 2 => new DateTime(today.Year, 1, 1), _ => DateTime.MinValue };
            var entries = journal.Entries.Where(entry => entry.Finished?.LocalDateTime >= start).ToArray();
            grid.Columns.Clear();
            void Columns(params string[] labels)
            {
                foreach (var label in labels) grid.Columns.Add($"col{grid.Columns.Count}", label);
            }
            switch (view.SelectedIndex)
            {
                case 1:
                    Columns(L("País", "Country"), L("Visitas", "Visits"), L("Entregas", "Deliveries"), L("Distancia observada", "Observed distance"));
                    var visits = entries.SelectMany(entry => entry.Measurements?.Countries ?? []).GroupBy(code => code).ToDictionary(group => group.Key, group => group.Count());
                    var destinations = entries.Where(entry => entry.Measurements?.DestinationCountry is not null)
                        .GroupBy(entry => entry.Measurements!.DestinationCountry!).ToDictionary(group => group.Key, group => group.Count());
                    var observed = entries.SelectMany(entry => entry.Measurements?.CountryKm?.AsEnumerable() ?? [])
                        .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));
                    foreach (var code in visits.Keys.Union(destinations.Keys).Union(observed.Keys).Order())
                        grid.Rows.Add(Countries.Name(code, English) ?? code, visits.GetValueOrDefault(code), destinations.GetValueOrDefault(code),
                            observed.TryGetValue(code, out var distance) ? Km(distance) : "—");
                    break;
                case 2:
                    Columns(L("Ciudad", "City"), L("Salidas", "Departures"), L("Destinos", "Destinations"));
                    var cities = entries.SelectMany(entry => new[] { (entry.Origin, 1, 0), (entry.Destination, 0, 1) });
                    foreach (var group in cities.GroupBy(item => item.Item1, StringComparer.CurrentCultureIgnoreCase).OrderByDescending(group => group.Sum(item => item.Item3)))
                        grid.Rows.Add(group.Key, group.Sum(item => item.Item2), group.Sum(item => item.Item3));
                    break;
                case 3:
                    Columns(L("Ruta", "Route"), L("Viajes", "Trips"), L("Distancia media", "Avg. distance"), L("Tiempo medio al volante", "Avg. driving time"));
                    foreach (var group in entries.GroupBy(Route, StringComparer.CurrentCultureIgnoreCase).OrderByDescending(group => group.Count()))
                        grid.Rows.Add(group.Key, group.Count(), Km(group.Average(entry => entry.Km)), FormatDriving(group.Average(entry => entry.DrivingSeconds)));
                    break;
                case 4:
                    Columns(L("Camión", "Truck"), L("Distancia", "Distance"), L("Al volante", "Driving"),
                        L("Entregas", "Deliveries"), "L/100 km", L("Viaje más largo", "Longest trip"));
                    foreach (var group in entries.GroupBy(entry => entry.Truck ?? "—").OrderByDescending(group => group.Sum(entry => entry.Km)))
                    {
                        var fuel = group.Where(entry => entry.Measurements?.Quality == "complete" && entry.FuelKm >= 20 && entry.FuelLitres > 0).ToArray();
                        grid.Rows.Add(group.Key, Km(group.Sum(entry => entry.Km)), FormatDriving(group.Sum(entry => entry.DrivingSeconds)),
                            Deliveries(group), fuel.Length == 0 ? "—" : $"{100 * fuel.Sum(entry => entry.FuelLitres) / fuel.Sum(entry => entry.FuelKm):N1}", Km(group.Max(entry => entry.Km)));
                    }
                    break;
                case 5:
                    Columns(L("Récord", "Record"), L("Valor", "Value"));
                    var longest = entries.MaxBy(entry => entry.Km);
                    var longestTime = entries.MaxBy(entry => entry.DrivingSeconds);
                    var fastest = entries.Select(entry => entry.Measurements?.MaximumKph).Max();
                    grid.Rows.Add(L("Mayor distancia", "Longest distance"), longest is null ? "—" : $"{Route(longest)} · {Km(longest.Km)}");
                    grid.Rows.Add(L("Mayor tiempo al volante", "Longest driving time"), longestTime is null ? "—" : $"{Route(longestTime)} · {FormatDriving(longestTime.DrivingSeconds)}");
                    grid.Rows.Add(L("Mayor velocidad observada", "Highest observed speed"), fastest is { } speed ? SpeedLabel(speed) : "—");
                    grid.Rows.Add(L("Ruta más recorrida", "Most travelled route"), MostCommon(entries.Select(Route)));
                    break;
                default:
                    Columns(L("Indicador", "Measure"), L("Valor", "Value"));
                    grid.Rows.Add(L("Entregas completadas", "Completed deliveries"), entries.Length == 0 ? "—" : $"{Deliveries(entries)}/{entries.Length} ({(double)Deliveries(entries) / entries.Length:P0})");
                    grid.Rows.Add(L("Distancia", "Distance"), Km(entries.Sum(entry => entry.Km)));
                    grid.Rows.Add(L("Al volante", "Driving"), FormatDriving(entries.Sum(entry => entry.DrivingSeconds)));
                    grid.Rows.Add(L("Distancia media", "Average distance"), entries.Length == 0 ? "—" : Km(entries.Average(entry => entry.Km)));
                    grid.Rows.Add(L("Ruta más frecuente", "Most frequent route"), MostCommon(entries.Select(Route)));
                    grid.Rows.Add(L("Camión más usado", "Most used truck"), MostCommon(entries.Select(entry => entry.Truck).OfType<string>()));
                    var month = new DateTime(today.Year, today.Month, 1);
                    var current = journal.Entries.Where(entry => entry.Finished?.LocalDateTime >= month).ToArray();
                    var previous = journal.Entries.Where(entry => entry.Finished?.LocalDateTime >= month.AddMonths(-1) && entry.Finished?.LocalDateTime < month).ToArray();
                    string Compare(double now, double then, string unit) => $"{now:N1} / {then:N1} {unit} · {now - then:+0.0;-0.0;0} {unit}"
                        + (then > 0 ? $" ({(now - then) / then:+0%;-0%;0%})" : "");
                    var factor = preferences.Features.Miles ? 1 / 1.609344 : 1;
                    grid.Rows.Add(L("Distancia · Este mes / Anterior", "Distance · This month / Previous"), Compare(current.Sum(entry => entry.Km) * factor, previous.Sum(entry => entry.Km) * factor, preferences.Features.Miles ? "mi" : "km"));
                    grid.Rows.Add(L("Horas · Este mes / Anterior", "Hours · This month / Previous"), Compare(current.Sum(entry => entry.DrivingSeconds) / 3600, previous.Sum(entry => entry.DrivingSeconds) / 3600, "h"));
                    grid.Rows.Add(L("Entregas · Este mes / Anterior", "Deliveries · This month / Previous"), Compare(Deliveries(current), Deliveries(previous), ""));
                    if (preferences.Features.MonthlyGoal is > 0 and var target)
                    {
                        var progress = preferences.Features.GoalDeliveries ? Deliveries(current) : current.Sum(entry => entry.Km);
                        var unit = preferences.Features.GoalDeliveries ? L("entregas", "deliveries") : "km";
                        grid.Rows.Add(L("Meta mensual", "Monthly goal"), $"{progress:N0} / {target:N0} {unit} ({progress / target:P0})");
                    }
                    break;
            }
        }
        period.SelectedIndexChanged += (_, _) => RefreshStatistics();
        view.SelectedIndexChanged += (_, _) => RefreshStatistics();
        charts.Click += (_, _) => ShowActivityCharts(window);
        goal.Click += (_, _) =>
        {
            var input = AskText(window, L("Meta mensual (5000 km, 20 entregas, o 0)", "Monthly goal (5000 km, 20 deliveries, or 0)"), "");
            if (input is null) return;
            var parts = input.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var target) || target is < 0 or > 1_000_000
                || parts.Length == 2 && parts[1] is not ("km" or "entregas" or "deliveries"))
            {
                MessageBox.Show(window, L("Usa una cantidad seguida de km o entregas.", "Enter an amount followed by km or deliveries."), Text);
                return;
            }
            SavePreferences(preferences with { Options = preferences.Features with { MonthlyGoal = target, GoalDeliveries = parts.Length == 2 && parts[1] != "km" } });
            RefreshStatistics();
        };
        window.Controls.Add(grid);
        window.Controls.Add(header);
        RefreshStatistics();
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private void ShowActivityCharts(IWin32Window owner)
    {
        using var window = ToolWindow(L("Actividad anual", "Annual activity"), new Size(810, 410));
        var header = Grid(1);
        header.Padding = new Padding(12);
        var year = new NumericUpDown { Minimum = 2000, Maximum = 2100, Value = DateTime.Today.Year,
            AccessibleName = L("Año", "Year") };
        var description = TextLabel("", 10);
        AddRows(header, year, description);
        var chart = new Panel { Dock = DockStyle.Fill, TabStop = false, AccessibleName = L("Calendario y kilómetros por mes", "Calendar and kilometres per month") };
        Dictionary<int, double> days = [];
        double[] months = new double[12];
        void RefreshChart()
        {
            days = journal.Entries.Where(entry => entry.Finished?.LocalDateTime.Year == year.Value)
                .GroupBy(entry => entry.Finished!.Value.LocalDateTime.DayOfYear - 1)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Km));
            Array.Clear(months);
            foreach (var (day, km) in days) months[new DateTime((int)year.Value, 1, 1).AddDays(day).Month - 1] += km;
            description.Text = L($"Cada cuadro es un día. Total del año {months.Sum():N0} km.",
                $"Each square is a day. Annual total {months.Sum():N0} km.");
            chart.Invalidate();
        }
        chart.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using var bar = new SolidBrush(Accent);
            var side = Math.Max(3, Math.Min(14, (chart.Width - 24) / 53));
            var dayCount = DateTime.IsLeapYear((int)year.Value) ? 366 : 365;
            var maxDay = Math.Max(1, days.Values.DefaultIfEmpty().Max());
            for (var day = 0; day < dayCount; day++)
            {
                var ratio = days.GetValueOrDefault(day) / maxDay;
                using var fill = new SolidBrush(ratio > 0 ? Color.FromArgb(70 + (int)(ratio * 185), Accent) : Color.FromArgb(28, Foreground));
                g.FillRectangle(fill, 12 + day / 7 * side, 8 + day % 7 * side, side - 1, side - 1);
            }
            var areaTop = 28 + side * 7;
            var baseline = chart.Height - 28;
            var barHeight = Math.Max(1, baseline - areaTop);
            var maxMonth = Math.Max(1, months.Max());
            var column = Math.Max(18, (chart.Width - 24) / 12);
            for (var index = 0; index < 12; index++)
            {
                var height = (int)(barHeight * months[index] / maxMonth);
                var x = 12 + column * index;
                g.FillRectangle(bar, x, baseline - height, Math.Max(2, column - 10), height);
                var label = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(index + 1);
                TextRenderer.DrawText(g, label, chart.Font, new Point(x, baseline + 4), Foreground);
            }
        };
        chart.Resize += (_, _) => chart.Invalidate();
        year.ValueChanged += (_, _) => RefreshChart();
        window.Controls.Add(chart);
        window.Controls.Add(header);
        StyleTool(window);
        RefreshChart();
        window.ShowDialog(owner);
    }
}
