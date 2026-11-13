using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private bool replayOpen;
    private string? recordingPath;

    private void ShowRecordings()
    {
        using var window = ToolWindow(L("Repeticiones", "Replays"), new Size(660, 420), placement: "replays");
        var list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended,
            AccessibleName = L("Grabaciones locales", "Local recordings"), FormattingEnabled = true, HorizontalScrollbar = true };
        string Kind(RecordingItem item) => item.Metadata?.JourneyType switch
        {
            "coach" => L("Pasajeros", "Passengers"),
            "delivery" => L("Entrega", "Delivery"),
            "free" => L("Conducción libre", "Free driving"),
            _ => ""
        };
        list.Format += (_, e) =>
        {
            if (e.ListItem is RecordingItem item)
                e.Value = Kind(item) is { Length: > 0 } kind ? $"{kind} · {item}" : item.ToString();
        };
        var bar = ToolBar();
        var record = MakeButton("");
        var play = MakeButton(L("Reproducir", "Play"));
        var open = MakeButton(L("Abrir archivo…", "Open file…"));
        var delete = MakeButton(L("Eliminar", "Delete"));
        var pause = MakeButton(L("Pausar grabación", "Pause recording"));
        var marker = MakeButton(L("Marcar · Ctrl + Alt + F12", "Mark · Ctrl + Alt + F12"));
        var compare = MakeButton(L("Comparar dos", "Compare two"));
        var compress = MakeButton(L("Comprimir", "Compress"));
        var trash = MakeButton(L("Papelera", "Trash"));
        var favorite = MakeButton(L("Favorito", "Favorite"));
        var upgrade = MakeButton(L("Actualizar metadatos…", "Update metadata…"));
        bar.Controls.AddRange([record, pause, marker, play, compare, open, delete, compress, trash, favorite, upgrade]);
        var heading = Grid(1);
        heading.Padding = new Padding(12, 10, 12, 5);
        var info = TextLabel("", 10);
        var search = new TextBox { Dock = DockStyle.Top, PlaceholderText = L("Buscar ciudad, camión, fecha o marcador…", "Find city, truck, date or marker…") };
        AddRows(heading, TextLabel(L("Repeticiones", "Replays"), 23, true), info, search, bar);
        window.Controls.Add(list);
        window.Controls.Add(heading);
        RecordingItem[] items = [];
        void Filter()
        {
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(items.Where(item => (item.Search + " " + Kind(item)).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).Cast<object>().ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.EndUpdate();
            UpdateButtons();
        }
        void Populate()
        {
            try
            {
                LocalData.PruneRecordings(preferences.RetentionDays, recordingPath);
                var favorites = LocalData.RecordingFavorites();
                items = LocalData.RecordingFiles().Where(file => file != recordingPath)
                    .Select(file => RecordingItem.FromFile(file, favorites.Contains(LocalData.RecordingKey(file)))).ToArray();
                info.Text = recording is not null ? L("Grabando el viaje en segundo plano.", "Recording the trip in the background.")
                    : items.Length == 0 ? L("Sin repeticiones · Inicia una grabación o abre un archivo.", "No replays · Start recording or open a file.")
                    : L("Elige un archivo para volver a recorrer el viaje.", "Choose a file to revisit the trip.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                info.Text = L("No se pudo abrir la carpeta de grabaciones.", "The recordings folder could not be opened.");
            }
            record.Text = recording is null ? L("Empezar a grabar", "Start recording") : L("Detener grabación", "Stop recording");
            Filter();
        }
        void UpdateButtons()
        {
            play.Enabled = delete.Enabled = list.SelectedItems.Count == 1;
            favorite.Enabled = play.Enabled;
            upgrade.Enabled = play.Enabled && list.SelectedItem is RecordingItem { Metadata.JourneyType: null } or RecordingItem { Metadata: null };
            favorite.Text = list.SelectedItem is RecordingItem { Favorite: true } ? L("Quitar favorito", "Remove favorite") : L("Marcar favorito", "Mark favorite");
            compare.Enabled = list.SelectedItems.Count == 2;
            compress.Enabled = list.SelectedItems.Count == 1 && list.SelectedItem is RecordingItem selected
                && selected.Path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
            pause.Enabled = recording is not null;
            marker.Enabled = recording is not null && !recordingPaused;
            pause.Text = recordingPaused ? L("Reanudar grabación", "Resume recording") : L("Pausar grabación", "Pause recording");
        }
        record.Click += (_, _) => { ToggleRecording(window); Populate(); };
        search.TextChanged += (_, _) => Filter();
        favorite.Click += (_, _) =>
        {
            if (list.SelectedItem is RecordingItem item && DataAction(window, () => LocalData.SetRecordingFavorite(item.Path, !item.Favorite))) Populate();
        };
        upgrade.Click += (_, _) =>
        {
            if (list.SelectedItem is not RecordingItem item) return;
            if (DataAction(window, () =>
            {
                var replay = TelemetryReplay.ReadDetailed(item.Path);
                var copy = Path.Combine(LocalData.Recordings, $"ETS2-metadata-{Guid.NewGuid():N}.jsonl");
                TelemetryReplay.Save(copy, replay.Frames, replay.Metadata?.StartedAt ?? replay.Metadata?.Created ?? item.Date);
            })) Populate();
        };
        list.SelectedIndexChanged += (_, _) => UpdateButtons();
        pause.Click += (_, _) => { PauseRecording(); UpdateButtons(); };
        marker.Click += (_, _) => AddRecordingMarker();
        trash.Click += (_, _) => { ShowRecordingTrash(window); Populate(); };
        compare.Click += (_, _) =>
        {
            if (list.SelectedItems.Cast<RecordingItem>().ToArray() is [var first, var second])
                CompareReplays(first.Path, second.Path, window);
        };
        void PlaySelected()
        {
            if (list.SelectedItems.Count == 1 && list.SelectedItem is RecordingItem item) ShowPlayback(item.Path, window);
        }
        play.Click += (_, _) => PlaySelected();
        list.DoubleClick += (_, _) => PlaySelected();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) play.PerformClick();
            else if (e.KeyCode == Keys.Delete) delete.PerformClick();
            else if (e.Control && e.KeyCode == Keys.A)
                for (var index = 0; index < list.Items.Count; index++) list.SetSelected(index, true);
            else return;
            e.SuppressKeyPress = true;
        };
        using var menu = new ContextMenuStrip();
        foreach (var button in new[] { play, compare, favorite, delete, upgrade })
            menu.Items.Add(button.Text, null, (_, _) => button.PerformClick());
        menu.Opening += (_, _) =>
        {
            var buttons = new[] { play, compare, favorite, delete, upgrade };
            for (var index = 0; index < buttons.Length; index++)
            {
                menu.Items[index].Text = buttons[index].Text;
                menu.Items[index].Enabled = buttons[index].Enabled;
            }
        };
        list.ContextMenuStrip = menu;
        open.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "JSONL (*.jsonl;*.jsonl.gz)|*.jsonl;*.jsonl.gz" };
            if (picker.ShowDialog(window) == DialogResult.OK) ShowPlayback(picker.FileName, window);
        };
        compress.Click += (_, _) =>
        {
            if (list.SelectedItem is not RecordingItem item || !compress.Enabled || item.Path == recordingPath) return;
            if (DataAction(window, () =>
            {
                var compressed = item.Path + ".gz";
                if (File.Exists(compressed)) throw new IOException("Compressed replay already exists.");
                var source = TelemetryReplay.ReadDetailed(item.Path);
                if (source.RecoveredTail) throw new InvalidDataException("Repair the partial replay before compressing it.");
                TelemetryReplay.Save(compressed, source.Frames, source.Metadata?.StartedAt ?? source.Metadata?.Created ?? item.Date);
                _ = TelemetryReplay.Read(compressed);
                File.Delete(item.Path);
            })) Populate();
        };
        delete.Click += (_, _) =>
        {
            if (list.SelectedItems.Count != 1 || list.SelectedItem is not RecordingItem item || !Confirm(window,
                L("¿Enviar esta grabación a la papelera?", "Move this recording to the trash?"))) return;
            DataAction(window, () => LocalData.TrashRecording(item.Path));
            Populate();
        };
        Populate();
        StyleTool(window);
        window.ShowDialog(this);
    }

    private sealed record RecordingItem(string Path, TelemetryReplay.RecordingMetadata? Metadata, DateTimeOffset Date, long Bytes, bool Favorite)
    {
        public string Search => $"{this} {Metadata?.Truck} {Date:yyyy-MM-dd} {string.Join(' ', Metadata?.MarkerTitles ?? [])}";
        public override string ToString() => $"{(Favorite ? "★ " : "")}{Date.LocalDateTime:g} · {Bytes / 1_000_000d:N2} MB" +
            (Metadata?.SchemaVersion == 1 ? " · ↺" : "") +
            (Metadata?.Duration is { } duration && duration >= 0 && duration <= 604_800_000
                ? $" · {ReplayTime(duration)} · {Metadata.Origin ?? "—"} → {Metadata.Destination ?? "—"} · ◆ {Metadata.MarkersCount ?? 0}"
                : $" · {System.IO.Path.GetFileNameWithoutExtension(Path)}");

        public static RecordingItem FromFile(string path, bool favorite)
        {
            var info = new FileInfo(path);
            TelemetryReplay.RecordingMetadata? metadata;
            try { metadata = TelemetryReplay.ReadHeader(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                metadata = null;
            }
            return new(path, metadata, metadata?.StartedAt ?? metadata?.Created ?? info.LastWriteTimeUtc, info.Length, favorite);
        }
    }

    private void ShowPlayback(string path, IWin32Window owner)
    {
        TelemetryReplay.RecordingRead? read = null;
        if (!DataAction(owner, () => read = TelemetryReplay.ReadDetailed(path)) || read is null) return;
        var data = read.Frames;
        using var dialog = new ReplayDialog();
        var window = ToolWindow(L("Repetición del viaje", "Trip replay"), new Size(885, 665), dialog, "playback");
        var routeName = string.Join(" → ", new[] { read.Metadata?.Origin, read.Metadata?.Destination }
            .Where(city => !string.IsNullOrWhiteSpace(city)));
        var routeTitle = read.Metadata?.JourneyType switch
        {
            "coach" => L("Ruta de pasajeros", "Passenger route"),
            "delivery" => L("Entrega", "Delivery"),
            "free" => L("Conducción libre", "Free driving"),
            _ => ""
        };
        var replayHeading = TextLabel(routeName.Length > 0 ? routeName : Path.GetFileNameWithoutExtension(path), 23, true);
        void WrapHeading() => SetWrap(replayHeading, window.ClientSize.Width - 16);
        window.ClientSizeChanged += (_, _) => WrapHeading();
        WrapHeading();
        replayHeading.Padding = new Padding(8, 4, 8, 4);
        var replayHeader = Grid(1);
        AddRows(replayHeader, replayHeading);
        if (routeTitle.Length > 0)
        {
            var category = TextLabel(routeTitle, 10);
            category.Padding = new Padding(8, 0, 8, 0);
            AddRows(replayHeader, category);
        }
        using var ticker = new System.Windows.Forms.Timer { Interval = 100 };
        var map = new TripMap { Dock = DockStyle.Fill };
        map.AccessibleDescription = L("Haz doble clic en el recorrido visible para saltar a ese momento.", "Double-click the visible route to seek to that time.");
        var graph = new SpeedChart(data, Surface, Foreground, Accent)
        {
            AccessibleName = L("Gráfico de velocidad. Pulsa para saltar a ese momento.", "Speed chart. Click to seek."),
            Height = 80, Dock = DockStyle.Top
        };
        var slider = new TrackBar { Maximum = (int)data[^1].ElapsedMs, TickStyle = TickStyle.None,
            SmallChange = 1000, LargeChange = 10_000, Dock = DockStyle.Top, AccessibleName = L("Tiempo de reproducción", "Replay time") };
        using var eventTips = new ToolTip { InitialDelay = 200 };
        var eventTicks = new Panel { Height = 22, Dock = DockStyle.Top, TabStop = false, AccessibleName = L("Eventos de la grabación", "Recording events") };
        (long Elapsed, string Kind)[] ticks = [];
        long? activeMoment = null;
        int TickX(long time) => 12 + (int)((double)time * Math.Max(1, eventTicks.Width - 24) / Math.Max(1, slider.Maximum));
        eventTicks.Paint += (_, e) =>
        {
            foreach (var cluster in ticks.GroupBy(item => TickX(item.Elapsed) / 14))
            {
                var (elapsed, kind) = cluster.First();
                var x = TickX(elapsed);
                if (cluster.Count() > 1)
                {
                    TextRenderer.DrawText(e.Graphics, $"×{cluster.Count()}", Font, new Point(x - 4, 2), Foreground);
                    continue;
                }
                var (color, height) = kind switch
                {
                    "pickup" => (Accent, 14), "delivery" => (Color.SeaGreen, 16), "ferry" => (Color.CornflowerBlue, 11),
                    "fuel" => (Color.DarkOrange, 13), "route_changed" => (Color.MediumVioletRed, 15), _ => (Foreground, 8)
                };
                using var stroke = new Pen(color, 2);
                using var fill = new SolidBrush(color);
                if (kind is "pickup" or "delivery")
                {
                    var direction = kind == "pickup" ? 1 : -1;
                    e.Graphics.FillPolygon(fill, [new Point(x, 10 - direction * 5), new Point(x - 4, 10 + direction * 4), new Point(x + 4, 10 + direction * 4)]);
                }
                else if (kind == "fuel")
                {
                    e.Graphics.DrawLine(stroke, x - 4, 8, x + 4, 8);
                    e.Graphics.DrawLine(stroke, x, 4, x, 12);
                }
                else if (kind == "route_changed")
                    e.Graphics.DrawEllipse(stroke, x - 3, 4, 6, 6);
                else
                    e.Graphics.DrawLine(stroke, x, 3, x, height);
            }
            if (activeMoment is { } selected)
            {
                using var ring = new Pen(Foreground, 2);
                e.Graphics.DrawEllipse(ring, TickX(selected) - 6, 1, 12, 16);
            }
            using var cursor = new Pen(Accent, 2);
            var current = TickX(slider.Value);
            e.Graphics.DrawLine(cursor, current, 0, current, eventTicks.Height);
        };
        eventTicks.MouseMove += (_, e) =>
        {
            var nearest = ticks.Where(item => Math.Abs(TickX(item.Elapsed) - e.X) <= 12)
                .OrderBy(item => Math.Abs(TickX(item.Elapsed) - e.X)).Take(1).ToArray();
            var text = nearest is [var item] ? $"{ReplayTime(item.Elapsed)} · {EventName(item.Kind)}" : "";
            if (eventTips.GetToolTip(eventTicks) != text) eventTips.SetToolTip(eventTicks, text);
        };
        var bar = ToolBar();
        var play = MakeButton(L("Pausar", "Pause"));
        var speed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 76, AccessibleName = L("Velocidad de reproducción", "Playback speed") };
        speed.Items.AddRange(["0.25×", "0.5×", "1×", "2×", "4×", "8×"]);
        speed.SelectedIndex = 2;
        var moments = new ListBox { Dock = DockStyle.Right, Width = 248, HorizontalScrollbar = true,
            AccessibleName = L("Eventos del viaje", "Trip events") };
        void SizeEvents() => moments.Width = Math.Clamp(window.ClientSize.Width / 3, 160, 300);
        window.ClientSizeChanged += (_, _) => SizeEvents();
        SizeEvents();
        string EventName(string kind) => kind switch
        {
            "pickup" => L("Recogida", "Pickup"), "ferry" => "Ferry", "delivery" => L("Entrega", "Delivery"),
            "fuel" => L("Repostaje", "Refueling"), "rest" => L("Descanso", "Rest"),
            "route_changed" => L("Ruta recalculada", "Route recalculated"),
            _ when kind.StartsWith("border:", StringComparison.Ordinal) => L("Frontera", "Border") + $" · {Countries.Name(kind[7..], English) ?? kind[7..]}",
            _ => kind
        };
        void ListMoments()
        {
            ticks = TelemetryReplay.Milestones(data).ToArray();
            moments.Items.Clear();
            moments.Items.AddRange(ticks.Select(item => new ReplayMoment(item.Elapsed, EventName(item.Kind), TripMap.EventSymbol(item.Kind))).Cast<object>().ToArray());
            moments.Enabled = moments.Items.Count > 0;
            eventTicks.Invalidate();
        }
        ListMoments();
        var trim = MakeButton(L("Recortar…", "Trim…"));
        trim.Click += (_, _) => TrimReplay(path, data, slider.Value, window);
        var rename = MakeButton(L("Nombrar marcador…", "Name marker…"));
        var deleteMarkers = MakeButton(L("Eliminar marcadores…", "Delete bookmarks…"));
        var exportMarkers = MakeButton(L("Exportar marcadores…", "Export bookmarks…"));
        var startedAt = read.Metadata?.StartedAt ?? read.Metadata?.Created ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        deleteMarkers.Click += (_, _) => { if (RemoveMarkers(path, data, window, startedAt)) ListMoments(); };
        exportMarkers.Click += (_, _) => ExportMarkers(data, window);
        rename.Enabled = data.Any(frame => !string.IsNullOrWhiteSpace(frame.Marker));
        rename.Click += (_, _) =>
        {
            if (RenameMarker(path, data, window, startedAt)) ListMoments();
        };
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 136, AccessibleName = L("Datos del gráfico", "Chart data") };
        mode.Items.AddRange([L("Velocidad", "Speed"), L("Combustible", "Fuel"), L("Distancia restante", "Remaining distance")]);
        mode.SelectedIndex = 0;
        var jump = MakeButton(L("Ir a momento…", "Go to time…"));
        var setA = MakeButton(L("Marcar A", "Set A"));
        var setB = MakeButton(L("Marcar B", "Set B"));
        var loop = new CheckBox { Text = L("Repetir A–B", "Loop A–B"), AutoSize = true, Enabled = false, FlatStyle = FlatStyle.Flat };
        using var extra = new ContextMenuStrip();
        var more = MakeButton("⋯");
        more.AccessibleName = L("Más acciones de reproducción", "More replay actions");
        foreach (var action in new[] { trim, rename, deleteMarkers, exportMarkers })
        {
            var item = extra.Items.Add(action.Text, null, (_, _) => action.PerformClick());
            extra.Opening += (_, _) => item.Enabled = action.Enabled;
            window.Disposed += (_, _) => action.Dispose();
        }
        more.Click += (_, _) => extra.Show(more, new Point(0, more.Height));
        var playback = ToolBar();
        playback.Controls.AddRange([play, speed]);
        bar.Controls.AddRange([setA, setB, loop, jump, mode, more]);
        if (read.RecoveredTail)
        {
            var repair = MakeButton(L("Guardar copia reparada…", "Save repaired copy…"));
            repair.Click += (_, _) =>
            {
                using var picker = new SaveFileDialog { Filter = "JSONL (*.jsonl)|*.jsonl", FileName = "ETS2-reparada.jsonl" };
                if (picker.ShowDialog(window) == DialogResult.OK && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(picker.FileName), StringComparison.OrdinalIgnoreCase))
                    DataAction(window, () => TelemetryReplay.Save(picker.FileName, data));
            };
            bar.Controls.Add(repair);
        }
        var mapTools = MapControls(map);
        var status = TextLabel("", 10, true);
        var replayPosition = TextLabel("", 10, true);
        var replayDuration = TextLabel(ReplayTime(slider.Maximum), 10, true);
        replayDuration.TextAlign = ContentAlignment.MiddleRight;
        var times = Grid(2);
        times.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        times.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        times.Controls.Add(replayPosition, 0, 0);
        times.Controls.Add(replayDuration, 1, 0);
        var loopDuration = TextLabel("", 9);
        loopDuration.Visible = false;
        var footer = Grid(1);
        footer.Padding = new Padding(8, 0, 8, 4);
        slider.AutoSize = false;
        slider.Height = 28;
        slider.Margin = Padding.Empty;
        if (read.RecoveredTail)
            AddRows(footer, TextLabel(L($"Se recuperaron {data.Count} de {data.Count + 1} muestras.", $"Recovered {data.Count} of {data.Count + 1} samples."), 10, true));
        var units = TextLabel(L("Velocidad · km/h", "Speed · km/h"), 9);
        status.Margin = units.Margin = new Padding(0, 0, 0, 4);
        mode.SelectedIndexChanged += (_, _) =>
        {
            graph.SetMetric(mode.SelectedIndex);
            units.Text = mode.SelectedIndex switch { 1 => L("Combustible · L", "Fuel · L"),
                2 => L("Distancia restante · km", "Remaining distance · km"), _ => L("Velocidad · km/h", "Speed · km/h") };
        };
        AddRows(footer, status, units, graph, eventTicks, slider, times, playback, loopDuration, bar);
        window.Controls.Add(map);
        window.Controls.Add(moments);
        window.Controls.Add(mapTools);
        window.Controls.Add(replayHeader);
        window.Controls.Add(footer);
        footer.Dock = DockStyle.Bottom;
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalMilliseconds;
        var elapsedMs = 0d;
        double[] multipliers = [.25, .5, 1, 2, 4, 8];
        int? loopStart = null;
        int? loopEnd = null;
        var maxSpeed = data.Select(frame => frame.Trip?.Signals?.Speed)
            .Where(value => value is >= -100 and <= 100).Select(value => Math.Abs(value!.Value) * 3.6).DefaultIfEmpty(-1).Max();
        var playing = slider.Maximum > 0;
        var shownIndex = int.MinValue;
        void Display()
        {
            slider.Value = Math.Clamp((int)elapsedMs, 0, slider.Maximum);
            var index = TelemetryReplay.IndexAt(data, slider.Value);
            var trip = index < 0 ? null : data[index].Trip;
            if (index != shownIndex)
            {
                map.UpdateMap(trip, cities, English, isLight, false);
                if (index >= 0) map.ShowReplayTrail(data, index);
                shownIndex = index;
            }
            map.UpdateReplayTruck(preferences.Features.ReduceMotion ? trip : TelemetryReplay.Interpolate(data, index, slider.Value));
            graph.SetElapsed(slider.Value);
            eventTicks.Invalidate();
            var velocity = trip?.Signals is { Speed: var value } && float.IsFinite(value)
                ? $" · {Math.Abs(value * 3.6):N0} km/h" : "";
            var maximum = maxSpeed >= 0 ? $" · {L("Máxima", "Top speed")} {SpeedLabel(maxSpeed)}" : "";
            replayPosition.Text = ReplayTime(slider.Value);
            status.Text = $"{L("Reproducción local", "Local replay")}{velocity}{maximum}";
            play.Text = playing ? L("Pausar", "Pause") : L("Reproducir", "Play");
        }
        void Seek(long at)
        {
            elapsedMs = Math.Clamp(at, 0, slider.Maximum);
            last = clock.Elapsed.TotalMilliseconds;
            Display();
        }
        play.Enabled = slider.Maximum > 0;
        play.Click += (_, _) =>
        {
            if (!playing && elapsedMs >= slider.Maximum) Seek(0);
            playing = !playing;
            last = clock.Elapsed.TotalMilliseconds;
            Display();
        };
        slider.Scroll += (_, _) => Seek(slider.Value);
        eventTicks.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var nearest = ticks.Where(item => Math.Abs(TickX(item.Elapsed) - e.X) <= 12)
                .OrderBy(item => Math.Abs(TickX(item.Elapsed) - e.X)).FirstOrDefault();
            if (nearest.Kind is not null)
                moments.SelectedIndex = Array.IndexOf(ticks, nearest);
        };
        map.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && map.ReplayTimeNear(e.Location, data, shownIndex) is { } at) Seek(at);
        };
        graph.MouseDown += (_, e) => Seek(graph.TimeAt(e.X));
        graph.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left) Seek(graph.TimeAt(e.X)); };
        jump.Click += (_, _) =>
        {
            var value = AskText(window, L("Ir a momento (hh:mm:ss)", "Go to time (hh:mm:ss)"), ReplayTime(slider.Value));
            if (value is null) return;
            var parts = value.Split(':');
            if (parts is [var h, var m, var s] && uint.TryParse(h, out var hours) && uint.TryParse(m, out var minutes)
                && uint.TryParse(s, out var seconds) && minutes < 60 && seconds < 60
                && (hours * 3600L + minutes * 60L + seconds) * 1000L <= slider.Maximum)
                Seek((hours * 3600L + minutes * 60L + seconds) * 1000L);
            else MessageBox.Show(window, L("Introduce un momento válido dentro de la grabación.", "Enter a valid time within the recording."), Text);
        };
        void UpdateLoop()
        {
            setA.Text = loopStart is { } a ? $"A · {ReplayTime(a)}" : L("Marcar A", "Set A");
            setB.Text = loopEnd is { } b ? $"B · {ReplayTime(b)}" : L("Marcar B", "Set B");
            loop.Enabled = loopStart is { } from && loopEnd is { } to && from < to;
            if (!loop.Enabled) loop.Checked = false;
            graph.SetLoop(loop.Enabled ? loopStart : null, loop.Enabled ? loopEnd : null);
            loopDuration.Text = loop.Enabled ? L($"Tramo A–B · {ReplayTime(loopEnd!.Value - loopStart!.Value)}",
                $"A–B range · {ReplayTime(loopEnd!.Value - loopStart!.Value)}") : "";
            loopDuration.Visible = loop.Enabled;
        }
        setA.Click += (_, _) => { loopStart = slider.Value; UpdateLoop(); };
        setB.Click += (_, _) => { loopEnd = slider.Value; UpdateLoop(); };
        bool Bookmark()
        {
            var index = TelemetryReplay.IndexAt(data, slider.Value);
            if (index < 0 || data.Count >= TelemetryReplay.MaximumFrames) return false;
            var frame = data[index];
            var marker = new RecordedFrame(frame.ElapsedMs, frame.Trip, L("Marcador", "Bookmark"));
            data.Insert(index + 1, marker);
            if (!DataAction(window, () => TelemetryReplay.Save(path, data, startedAt)))
            {
                data.RemoveAt(index + 1);
                return false;
            }
            rename.Enabled = true;
            ListMoments();
            return true;
        }
        moments.SelectedIndexChanged += (_, _) =>
        {
            if (moments.SelectedItem is not ReplayMoment moment)
            {
                activeMoment = null;
                graph.Highlight(null);
                map.HighlightReplayEvent(null, null);
                eventTicks.Invalidate();
                return;
            }
            activeMoment = moment.Elapsed;
            graph.Highlight(moment.Elapsed);
            Seek(moment.Elapsed);
            var index = TelemetryReplay.IndexAt(data, moment.Elapsed);
            map.HighlightReplayEvent(index >= 0 ? data[index].Trip : null, moment.Symbol);
        };
        dialog.HandleShortcut = key =>
        {
            if (key == Keys.M) Bookmark();
            else if (key == Keys.Space) play.PerformClick();
            else if (key == Keys.Home) Seek(0);
            else if (key == Keys.End) Seek(slider.Maximum);
            else if (key is Keys.Left or Keys.Right or (Keys.Control | Keys.Left) or (Keys.Control | Keys.Right))
            {
                var forward = (key & Keys.KeyCode) == Keys.Right;
                if ((key & Keys.Control) != 0)
                {
                    var events = moments.Items.OfType<ReplayMoment>().Select(moment => moment.Elapsed);
                    Seek(forward ? events.FirstOrDefault(at => at > elapsedMs, slider.Maximum)
                        : events.LastOrDefault(at => at < elapsedMs, 0));
                }
                else Seek((long)elapsedMs + (forward ? 5000 : -5000));
            }
            else return false;
            return true;
        };
        ticker.Tick += (_, _) =>
        {
            var now = clock.Elapsed.TotalMilliseconds;
            if (playing)
            {
                elapsedMs = Math.Min(slider.Maximum, elapsedMs + (now - last) * multipliers[speed.SelectedIndex]);
                if (loop.Checked && loopStart is { } from && loopEnd is { } to && (elapsedMs < from || elapsedMs >= to))
                    elapsedMs = from;
                else if (elapsedMs >= slider.Maximum) playing = false;
                Display();
            }
            last = now;
        };
        StyleTool(window);
        Display();
        replayOpen = true;
        try
        {
            RefreshPresence();
            ticker.Start();
            window.ShowDialog(owner);
        }
        finally
        {
            ticker.Stop();
            replayOpen = false;
            RefreshPresence();
        }
    }

    private static string ReplayTime(long ms)
    {
        var time = TimeSpan.FromMilliseconds(ms);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    private string? AskText(IWin32Window owner, string title, string initial)
    {
        using var window = ToolWindow(title, new Size(380, 170));
        var content = Grid(1);
        content.Padding = new Padding(12);
        var input = new TextBox { Text = initial, AccessibleName = title, Dock = DockStyle.Top, MaxLength = 160 };
        var accept = MakeButton(L("Aceptar", "OK"));
        accept.DialogResult = DialogResult.OK;
        AddRows(content, input, accept);
        window.Controls.Add(content);
        window.AcceptButton = accept;
        StyleTool(window);
        return window.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }

    private sealed class ReplayDialog : Form
    {
        internal Func<Keys, bool>? HandleShortcut;

        protected override bool ProcessCmdKey(ref Message message, Keys keyData) =>
            HandleShortcut?.Invoke(keyData) == true || base.ProcessCmdKey(ref message, keyData);
    }

    private sealed record ReplayMoment(long Elapsed, string Title, string Symbol)
    {
        public override string ToString() => $"{Symbol}  {ReplayTime(Elapsed)} · {Title}";
    }

    private sealed class SpeedChart : Control
    {
        private readonly IReadOnlyList<RecordedFrame> frames;
        private readonly Color background;
        private readonly Color ink;
        private readonly Color accent;
        private readonly int maximum;
        private Bitmap? plot;
        private long elapsed;
        private long? highlighted;
        public void Highlight(long? at) { highlighted = at; Invalidate(); }
        private int metric;
        private (int Start, int End)? loopRange;
        private float ceiling = 180;

        public void SetMetric(int mode)
        {
            metric = mode;
            ceiling = mode == 0 ? 180 : Math.Max(3, (float)Math.Ceiling(frames.Select(Value).OfType<float>().DefaultIfEmpty().Max() / 30) * 30);
            plot?.Dispose();
            plot = null;
            Invalidate();
        }

        private float? Value(RecordedFrame frame) => metric switch
        {
            1 => frame.Trip?.Truck?.Litres is >= 0 and < 20_000 ? frame.Trip.Truck.Litres : null,
            2 => frame.Trip?.RemainingKm is { } km ? (float)km : null,
            _ => frame.Trip?.Signals is { Speed: >= -100 and <= 100 } signal ? Math.Abs(signal.Speed) * 3.6f : null
        };

        public SpeedChart(IReadOnlyList<RecordedFrame> frames, Color background, Color ink, Color accent)
        {
            this.frames = frames;
            this.background = background;
            this.ink = ink;
            this.accent = accent;
            maximum = (int)Math.Max(1, frames[^1].ElapsedMs);
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public void SetElapsed(long value)
        {
            elapsed = value;
            Invalidate();
        }

        public void SetLoop(int? start, int? end)
        {
            loopRange = start.HasValue && end.HasValue ? (start.Value, end.Value) : null;
            Invalidate();
        }

        private float PositionAt(long time) => 42 + time * (float)PlotWidth / maximum;
        private int PlotWidth => Math.Max(1, Width - 52);

        public long TimeAt(int x) => (long)(Math.Clamp((x - 42d) / PlotWidth, 0, 1) * maximum);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 0 || Height <= 0) return;
            if (plot?.Size != Size)
            {
                plot?.Dispose();
                plot = new Bitmap(Width, Height);
                using var drawing = Graphics.FromImage(plot);
                DrawPlot(drawing);
            }
            e.Graphics.DrawImageUnscaled(plot, 0, 0);
            if (loopRange is { } range)
            {
                using var rangeColor = new SolidBrush(Color.FromArgb(38, accent));
                var area = new RectangleF(PositionAt(range.Start), 4,
                    PositionAt(range.End) - PositionAt(range.Start), Math.Max(1, Height - 25));
                e.Graphics.FillRectangle(rangeColor, area);
                using var edge = new Pen(accent) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(edge, area.X, area.Y, area.Width, area.Height);
            }
            if (highlighted is { } time)
            {
                using var selected = new Pen(ink) { DashStyle = DashStyle.Dot };
                e.Graphics.DrawLine(selected, PositionAt(time), 4, PositionAt(time), Height - 20);
            }
            var cursor = PositionAt(elapsed);
            using var cursorPen = new Pen(accent, 2);
            e.Graphics.DrawLine(cursorPen, cursor, 4, cursor, Height - 20);
        }

        private void DrawPlot(Graphics g)
        {
            g.Clear(background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var height = Math.Max(1, Height - 29);
            using var grid = new Pen(Color.FromArgb(85, ink));
            using var trace = new Pen(accent, 2);
            for (var i = 0; i <= 3; i++)
            {
                var y = 7 + height * (3 - i) / 3;
                g.DrawLine(grid, 42, y, Width - 10, y);
                TextRenderer.DrawText(g, (i * ceiling / 3).ToString("0"), Font, new Point(4, y - 8), ink);
            }
            var ticks = Width < 340 ? 2 : 4;
            for (var i = 0; i <= ticks; i++)
            {
                var at = maximum * (long)i / ticks;
                var caption = ReplayTime(at);
                var width = TextRenderer.MeasureText(caption, Font).Width;
                var x = Math.Clamp((int)PositionAt(at) - width / 2, 0, Math.Max(0, Width - width));
                TextRenderer.DrawText(g, caption, Font, new Point(x, Height - 19), ink);
            }
            PointF? previous = null;
            long? missingSince = null;
            using var gap = new HatchBrush(HatchStyle.ForwardDiagonal, Color.FromArgb(100, ink), Color.Transparent);
            foreach (var frame in frames)
            {
                if (Value(frame) is not { } value)
                {
                    previous = null;
                    missingSince ??= frame.ElapsedMs;
                    continue;
                }
                if (missingSince is { } since)
                {
                    var left = PositionAt(since);
                    g.FillRectangle(gap, left, 7, Math.Max(1, PositionAt(frame.ElapsedMs) - left), height);
                    missingSince = null;
                }
                var point = new PointF(42 + frame.ElapsedMs * (float)PlotWidth / maximum,
                    7 + height * (1 - Math.Clamp(value, 0, ceiling) / ceiling));
                if (previous is { } last) g.DrawLine(trace, last, point);
                previous = point;
            }
            if (missingSince is { } remaining)
                g.FillRectangle(gap, PositionAt(remaining), 7, Math.Max(1, PositionAt(maximum) - PositionAt(remaining)), height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) plot?.Dispose();
            base.Dispose(disposing);
        }
    }
}
