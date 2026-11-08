using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private bool replayOpen;
    private string? recordingPath;

    private void ShowRecordings()
    {
        using var window = ToolWindow(L("Repeticiones", "Replays"), new Size(660, 420));
        var list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, AccessibleName = L("Grabaciones locales", "Local recordings") };
        var bar = ToolBar();
        var record = MakeButton("");
        var play = MakeButton(L("Reproducir", "Play"));
        var open = MakeButton(L("Abrir archivo…", "Open file…"));
        var delete = MakeButton(L("Eliminar", "Delete"));
        var pause = MakeButton(L("Pausar grabación", "Pause recording"));
        var marker = MakeButton(L("Marcar · Ctrl + Alt + F12", "Mark · Ctrl + Alt + F12"));
        var compare = MakeButton(L("Comparar dos", "Compare two"));
        var trash = MakeButton(L("Papelera", "Trash"));
        bar.Controls.AddRange([record, pause, marker, play, compare, open, delete, trash]);
        var heading = Grid(1);
        heading.Padding = new Padding(12, 10, 12, 5);
        var info = TextLabel("", 10);
        AddRows(heading, TextLabel(L("Grabaciones locales", "Local recordings"), 18, true), info, bar);
        window.Controls.Add(list);
        window.Controls.Add(heading);
        void Populate()
        {
            list.Items.Clear();
            try
            {
                LocalData.PruneRecordings(preferences.RetentionDays, recordingPath);
                foreach (var file in LocalData.RecordingFiles().Where(file => file != recordingPath))
                    list.Items.Add(new RecordingItem(file));
                info.Text = recording is not null ? L("Grabando el viaje en segundo plano.", "Recording the trip in the background.")
                    : list.Items.Count == 0 ? L("Inicia una grabación o abre un archivo JSONL.", "Start recording or open a JSONL file.")
                    : L("Elige un archivo para volver a recorrer el viaje.", "Choose a file to revisit the trip.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                info.Text = L("No se pudo abrir la carpeta de grabaciones.", "The recordings folder could not be opened.");
            }
            record.Text = recording is null ? L("Empezar a grabar", "Start recording") : L("Detener grabación", "Stop recording");
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            UpdateButtons();
        }
        void UpdateButtons()
        {
            play.Enabled = delete.Enabled = list.SelectedItems.Count == 1;
            compare.Enabled = list.SelectedItems.Count == 2;
            pause.Enabled = recording is not null;
            marker.Enabled = recording is not null && !recordingPaused;
            pause.Text = recordingPaused ? L("Reanudar grabación", "Resume recording") : L("Pausar grabación", "Pause recording");
        }
        record.Click += (_, _) => { ToggleRecording(window); Populate(); };
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
        open.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "JSONL (*.jsonl)|*.jsonl" };
            if (picker.ShowDialog(window) == DialogResult.OK) ShowPlayback(picker.FileName, window);
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

    private sealed record RecordingItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    private void ShowPlayback(string path, IWin32Window owner)
    {
        List<RecordedFrame>? frames = null;
        if (!DataAction(owner, () => frames = TelemetryReplay.Read(path)) || frames is null) return;
        var data = frames;
        using var window = ToolWindow(L("Reproducción del viaje", "Trip replay"), new Size(885, 665));
        using var ticker = new System.Windows.Forms.Timer { Interval = 100 };
        var map = new TripMap { Dock = DockStyle.Fill };
        var graph = new SpeedChart(data, Surface, Foreground, Accent)
        {
            AccessibleName = L("Gráfico de velocidad. Pulsa para saltar a ese momento.", "Speed chart. Click to seek."),
            Height = 96, Dock = DockStyle.Top
        };
        var slider = new TrackBar { Maximum = (int)data[^1].ElapsedMs, TickStyle = TickStyle.None,
            SmallChange = 1000, LargeChange = 10_000, Dock = DockStyle.Top, AccessibleName = L("Tiempo de reproducción", "Replay time") };
        var bar = ToolBar();
        var play = MakeButton(L("Pausar", "Pause"));
        var speed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 76, AccessibleName = L("Velocidad de reproducción", "Playback speed") };
        speed.Items.AddRange(["0.5×", "1×", "2×", "4×", "8×"]);
        speed.SelectedIndex = 1;
        var moments = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 225, AccessibleName = L("Saltar a un momento del viaje", "Jump to a milestone") };
        moments.Items.Add(L("Saltar a un momento…", "Jump to a milestone…"));
        void ListMoments()
        {
            while (moments.Items.Count > 1) moments.Items.RemoveAt(1);
            foreach (var (elapsed, kind) in TelemetryReplay.Milestones(data))
                moments.Items.Add(new ReplayMoment(elapsed, kind switch
                {
                    "pickup" => L("Recogida", "Pickup"), "ferry" => "Ferry", "delivery" => L("Entrega", "Delivery"), _ => kind
                }));
            moments.SelectedIndex = 0;
            moments.Enabled = moments.Items.Count > 1;
        }
        ListMoments();
        var trim = MakeButton(L("Recortar…", "Trim…"));
        trim.Click += (_, _) => TrimReplay(path, data, slider.Value, window);
        var rename = MakeButton(L("Nombrar marcador…", "Name marker…"));
        rename.Enabled = data.Any(frame => !string.IsNullOrWhiteSpace(frame.Marker));
        rename.Click += (_, _) =>
        {
            if (RenameMarker(path, data, window)) ListMoments();
        };
        bar.Controls.AddRange([play, speed, moments, trim, rename]);
        var mapTools = MapControls(map);
        var status = TextLabel("", 11, true);
        var footer = Grid(1);
        footer.Padding = new Padding(10, 5, 10, 6);
        AddRows(footer, status, TextLabel(L("Velocidad · km/h", "Speed · km/h"), 9), graph, slider, bar);
        window.Controls.Add(map);
        window.Controls.Add(mapTools);
        window.Controls.Add(footer);
        footer.Dock = DockStyle.Bottom;
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalMilliseconds;
        var elapsedMs = 0d;
        double[] multipliers = [.5, 1, 2, 4, 8];
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
            graph.SetElapsed(slider.Value);
            var velocity = trip?.Signals is { Speed: var value } && float.IsFinite(value)
                ? $" · {Math.Abs(value * 3.6):N0} km/h" : "";
            status.Text = $"{ReplayTime(slider.Value)} / {ReplayTime(slider.Maximum)}{velocity}  ·  {L("Reproducción local", "Local replay")}";
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
        graph.MouseDown += (_, e) => Seek(graph.TimeAt(e.X));
        moments.SelectionChangeCommitted += (_, _) =>
        {
            if (moments.SelectedItem is ReplayMoment moment) Seek(moment.Elapsed);
            moments.SelectedIndex = 0;
        };
        ticker.Tick += (_, _) =>
        {
            var now = clock.Elapsed.TotalMilliseconds;
            if (playing)
            {
                elapsedMs = Math.Min(slider.Maximum, elapsedMs + (now - last) * multipliers[speed.SelectedIndex]);
                if (elapsedMs >= slider.Maximum) playing = false;
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

    private sealed record ReplayMoment(long Elapsed, string Title)
    {
        public override string ToString() => $"{ReplayTime(Elapsed)} · {Title}";
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
            var cursor = 42 + elapsed * (float)PlotWidth / maximum;
            using var cursorPen = new Pen(ink, 1);
            e.Graphics.DrawLine(cursorPen, cursor, 4, cursor, Height - 4);
        }

        private void DrawPlot(Graphics g)
        {
            g.Clear(background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var height = Math.Max(1, Height - 18);
            using var grid = new Pen(Color.FromArgb(85, ink));
            using var trace = new Pen(accent, 2);
            for (var i = 0; i <= 3; i++)
            {
                var y = 7 + height * (3 - i) / 3;
                g.DrawLine(grid, 42, y, Width - 10, y);
                TextRenderer.DrawText(g, (i * 60).ToString(), Font, new Point(4, y - 8), ink);
            }
            PointF? previous = null;
            foreach (var frame in frames)
            {
                if (frame.Trip?.Signals is not { Speed: var speed } || !float.IsFinite(speed))
                {
                    previous = null;
                    continue;
                }
                var point = new PointF(42 + frame.ElapsedMs * (float)PlotWidth / maximum,
                    7 + height * (1 - Math.Clamp(Math.Abs(speed * 3.6f), 0, 180) / 180));
                if (previous is { } last) g.DrawLine(trace, last, point);
                previous = point;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) plot?.Dispose();
            base.Dispose(disposing);
        }
    }
}
