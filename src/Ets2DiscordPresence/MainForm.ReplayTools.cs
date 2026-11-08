namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ShowRecordingTrash(IWin32Window owner)
    {
        using var window = ToolWindow(L("Papelera de grabaciones", "Recording trash"), new Size(570, 380));
        var list = new ListBox { Dock = DockStyle.Fill };
        var restore = MakeButton(L("Restaurar grabación", "Restore recording"));
        restore.Dock = DockStyle.Bottom;
        void LoadItems()
        {
            list.Items.Clear();
            DataAction(window, () => list.Items.AddRange(LocalData.TrashedRecordings().Select(path => new RecordingItem(path)).Cast<object>().ToArray()));
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            restore.Enabled = list.SelectedIndex >= 0;
        }
        restore.Click += (_, _) =>
        {
            if (list.SelectedItem is RecordingItem item && DataAction(window, () => LocalData.RestoreRecording(item.Path))) LoadItems();
        };
        window.Controls.Add(list);
        window.Controls.Add(restore);
        LoadItems();
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private void CompareReplays(string first, string second, IWin32Window owner)
    {
        List<RecordedFrame>[]? series = null;
        if (!DataAction(owner, () => series = [TelemetryReplay.Read(first), TelemetryReplay.Read(second)]) || series is null) return;
        var files = new[] { first, second };
        var progressSeries = series.Select(frames => frames.Where(frame => frame.Trip?.Progress.HasValue == true)
            .GroupBy(frame => frame.Trip!.Progress!.Value).OrderBy(group => group.Key)
            .Select(group => (Percent: group.Key, Elapsed: group.First().ElapsedMs)).ToArray()).ToArray();
        var useRouteProgress = progressSeries.All(samples => samples.Length >= 4);
        using var window = ToolWindow(L("Comparar repeticiones", "Compare replays"), new Size(1000, 520));
        var grid = Grid(2);
        grid.Dock = DockStyle.Fill;
        grid.AutoSize = false;
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var maps = new TripMap[2];
        var labels = new Label[2];
        for (var index = 0; index < 2; index++)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var caption = TextLabel(Path.GetFileNameWithoutExtension(files[index]), 10, true);
            caption.Dock = DockStyle.Top;
            maps[index] = new TripMap { Dock = DockStyle.Fill };
            labels[index] = TextLabel("", 10);
            labels[index].Dock = DockStyle.Bottom;
            panel.Controls.Add(maps[index]);
            panel.Controls.Add(caption);
            panel.Controls.Add(labels[index]);
            grid.Controls.Add(panel, index, 0);
        }
        var seek = new TrackBar { Dock = DockStyle.Bottom, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None,
            AccessibleName = L("Avance compartido", "Shared progress") };
        var progressLabel = TextLabel("", 10, true);
        progressLabel.Dock = DockStyle.Bottom;
        void Render()
        {
            var percent = seek.Value / 10d;
            progressLabel.Text = useRouteProgress ? L($"Avance de la entrega: {percent:N1} %", $"Delivery progress: {percent:N1} %")
                : L($"Tiempo de la grabación: {percent:N1} %", $"Recording time: {percent:N1} %");
            for (var i = 0; i < 2; i++)
            {
                var frames = series[i];
                var ms = useRouteProgress
                    ? progressSeries[i].MinBy(sample => Math.Abs(sample.Percent - percent)).Elapsed
                    : frames[^1].ElapsedMs * seek.Value / seek.Maximum;
                var at = TelemetryReplay.IndexAt(frames, ms);
                var trip = at >= 0 ? frames[at].Trip : null;
                maps[i].UpdateMap(trip, cities, English, isLight, false);
                if (at >= 0) maps[i].ShowReplayTrail(frames, at);
                labels[i].Text = $"{ReplayTime(ms)} / {ReplayTime(frames[^1].ElapsedMs)}"
                    + (trip?.Signals is { Speed: var speed } ? $" · {AppOptions.Distance(Math.Abs(speed * 3.6), preferences.Features.Miles)}/h" : "");
            }
        }
        seek.ValueChanged += (_, _) => Render();
        window.Controls.Add(grid);
        window.Controls.Add(seek);
        window.Controls.Add(progressLabel);
        StyleTool(window);
        Render();
        replayOpen = true;
        try { RefreshPresence(); window.ShowDialog(owner); }
        finally { replayOpen = false; RefreshPresence(); }
    }

    private void TrimReplay(string originalPath, IReadOnlyList<RecordedFrame> frames, int current, IWin32Window owner)
    {
        using var window = ToolWindow(L("Recortar grabación", "Trim recording"), new Size(500, 340));
        var layout = Grid(1);
        layout.Padding = new Padding(16);
        var endAt = frames[^1].ElapsedMs / 1000m;
        var from = new NumericUpDown { DecimalPlaces = 1, Maximum = endAt, Value = Math.Min(endAt, current / 1000m) };
        var to = new NumericUpDown { DecimalPlaces = 1, Maximum = endAt, Value = endAt };
        var save = MakeButton(L("Guardar copia…", "Save copy…"));
        AddRows(layout, TextLabel(L("Inicio en segundos", "Start in seconds"), 10, true), from,
            TextLabel(L("Final en segundos", "End in seconds"), 10, true), to, save);
        save.Click += (_, _) =>
        {
            if (from.Value >= to.Value) return;
            using var picker = new SaveFileDialog { Filter = "JSONL (*.jsonl)|*.jsonl", FileName = "ETS2-recorte.jsonl" };
            if (picker.ShowDialog(window) != DialogResult.OK) return;
            if (string.Equals(Path.GetFullPath(originalPath), Path.GetFullPath(picker.FileName), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(window, L("El recorte necesita un archivo nuevo.", "The trimmed replay needs a new file."), Text);
                return;
            }
            if (DataAction(window, () => TelemetryReplay.Save(picker.FileName,
                TelemetryReplay.Trim(frames, (long)(from.Value * 1000), (long)(to.Value * 1000))))) window.Close();
        };
        window.Controls.Add(layout);
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private bool RenameMarker(string file, List<RecordedFrame> data, IWin32Window owner)
    {
        using var window = ToolWindow(L("Nombrar marcador", "Name marker"), new Size(460, 310));
        var layout = Grid(1);
        layout.Padding = new Padding(16);
        var markers = data.Select((frame, index) => (frame, index)).Where(item => !string.IsNullOrWhiteSpace(item.frame.Marker)).ToArray();
        if (markers.Length == 0) return false;
        var choose = Choice(layout, L("Marcador", "Marker"), markers
            .Select(item => $"{ReplayTime(item.frame.ElapsedMs)} · {item.frame.Marker}").ToArray(), 0);
        var title = FeatureText(layout, L("Nombre nuevo", "New name"), markers[0].frame.Marker, 80);
        choose.SelectedIndexChanged += (_, _) => title.Text = markers[choose.SelectedIndex].frame.Marker;
        var save = MakeButton(L("Guardar", "Save"));
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(title.Text)) window.DialogResult = DialogResult.OK; };
        AddRows(layout, save);
        window.Controls.Add(layout);
        StyleTool(window);
        if (window.ShowDialog(owner) != DialogResult.OK) return false;
        var index = markers[choose.SelectedIndex].index;
        var original = data[index];
        data[index] = original with { Marker = title.Text.Trim() };
        if (DataAction(owner, () => TelemetryReplay.Save(file, data))) return true;
        data[index] = original;
        return false;
    }
}
