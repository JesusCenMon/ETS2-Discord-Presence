using System.Diagnostics;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private readonly UiPerformance uiPerformance = new();
    private readonly UiViewState viewState = new(Path.Combine(LocalData.Folder, "view-state.json"));
    private readonly System.Windows.Forms.Timer uiPulse = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer idleWarmup = new() { Interval = 2200 };
    private readonly HashSet<EventHandler> pendingIdleSamples = [];
    private long lastUiPulse;
    private long lastNavigationAt;
    private int navigationVersion;
    private CancellationTokenSource? warmupCancellation;
    private Dictionary<string, (long Bytes, DateTime Modified, RecordingItem Item)>? warmRecordings;
    private readonly Dictionary<string, (bool Available, long Until)> activityArtworkChecks = [];
    private readonly HashSet<string> artworkChecksPending = [];

    private string SelectActivityArtwork(string requested, bool coach)
    {
        if (demo || demoScenario is not null || replayOpen) return requested;
        var now = Environment.TickCount64;
        if (activityArtworkChecks.TryGetValue(requested, out var known) && now < known.Until)
        {
            if (known.Available) return requested;
            homeArtworkNote = L("Portada genérica · La portada del país no está disponible", "Generic artwork · Country artwork unavailable");
            return DiscordIdentity.ImageFor(null, coach: coach);
        }
        if (artworkChecksPending.Add(requested)) _ = CheckActivityArtworkAsync(requested);
        return requested;
    }

    private async Task CheckActivityArtworkAsync(string uri)
    {
        try
        {
            var available = await VerifyArtworkAsync(uri);
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            if (activityArtworkChecks.Count >= 128) activityArtworkChecks.Clear();
            activityArtworkChecks[uri] = (available, Environment.TickCount64 + (available ? 3_600_000 : 120_000));
            if (!available)
            {
                artworkFallbacks++;
                publishImmediately = true;
                RefreshPresence();
            }
        }
        finally { artworkChecksPending.Remove(uri); }
    }

    private void AddEmptyActions(Control surface, params (string Text, Action Run)[] actions)
    {
        var bar = ToolBar();
        bar.Dock = DockStyle.Bottom;
        bar.Padding = new Padding(16);
        foreach (var action in actions)
        {
            var button = MakeButton(action.Text);
            button.Click += (_, _) => action.Run();
            bar.Controls.Add(button);
        }
        surface.Controls.Add(bar);
    }

    private void StartQualityMonitor()
    {
        lastUiPulse = Environment.TickCount64;
        uiPulse.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            var delay = now - lastUiPulse - uiPulse.Interval;
            if (Visible && WindowState != FormWindowState.Minimized && delay < 30_000)
                uiPerformance.RecordStall(delay);
            lastUiPulse = now;
        };
        uiPulse.Start();
        idleWarmup.Tick += async (_, _) =>
        {
            if (!Visible || currentPage != "home" || Environment.TickCount64 - lastNavigationAt < 2000) return;
            idleWarmup.Stop();
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            warmupCancellation = cancel;
            try
            {
                var loaded = await Task.Run(() =>
                {
                    var cache = new Dictionary<string, (long, DateTime, RecordingItem)>(StringComparer.OrdinalIgnoreCase);
                    // Bounded prefetch: no controls, telemetry or game preparation on this worker.
                    foreach (var file in LocalData.RecordingFiles().Where(file => file != recordingPath).Take(32))
                    {
                        cancel.Token.ThrowIfCancellationRequested();
                        var info = new FileInfo(file);
                        cache[file] = (info.Length, info.LastWriteTimeUtc, RecordingItem.FromFile(file, false));
                    }
                    _ = DiscordIdentity.ImageFor(null);
                    return cache;
                }, cancel.Token);
                if (!IsDisposed && !cancel.IsCancellationRequested) warmRecordings = loaded;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException) { }
            finally { if (ReferenceEquals(warmupCancellation, cancel)) warmupCancellation = null; }
        };
        idleWarmup.Start();
    }

    private IDisposable MeasureNavigation(string section)
    {
        var cold = uiPerformance.Visit(section);
        var clock = Stopwatch.StartNew();
        return new OnDispose(() =>
        {
            var handler = clock.Elapsed.TotalMilliseconds;
            if (section == "telemetry-refresh" && handler < 50) return;
            var sample = uiPerformance.Record(section, handler, null, cold);
            if (section == "telemetry-refresh" || !IsHandleCreated || !Visible || IsDisposed)
            {
                return;
            }
            // Idle follows queued layout/paint messages. It is not a screen refresh/FPS measurement.
            var version = ++navigationVersion;
            EventHandler? settled = null;
            settled = (_, _) =>
            {
                Application.Idle -= settled;
                pendingIdleSamples.Remove(settled!);
                if (version == navigationVersion) uiPerformance.Settle(sample, clock.Elapsed.TotalMilliseconds);
            };
            if (pendingIdleSamples.Count >= 8)
            {
                var oldest = pendingIdleSamples.First();
                Application.Idle -= oldest;
                pendingIdleSamples.Remove(oldest);
            }
            pendingIdleSamples.Add(settled);
            Application.Idle += settled;
        });
    }

    private sealed class OnDispose(Action action) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; action(); }
    }

    private void StopQualityMonitor()
    {
        uiPulse.Dispose();
        idleWarmup.Dispose();
        warmupCancellation?.Cancel();
        foreach (var sample in pendingIdleSamples) Application.Idle -= sample;
        pendingIdleSamples.Clear();
        // Snapshot has already been captured on the UI thread; a bounded wait only on exit.
        viewState.SaveAsync().Wait(1500);
    }

    private string BuildDiagnosticReport() => string.Join(Environment.NewLine,
        $"ETS2 Discord Presence {DesktopServices.Version.ToString(3)}",
        $"OS {Environment.OSVersion.Version}; DPI {DeviceDpi}; text {preferences.TextPercent}%; theme {preferences.Theme}",
        $"Game running: {gameRunning}; telemetry: {liveSnapshot is not null}; reader API: {lastRevision?.ToString() ?? "—"}",
        $"Discord connected: {discordReady}; acknowledged: {discordConfirmed}; error: {lastDiscordError ?? "—"}",
        $"Last sent: {DiscordDiagnosticTime(Interlocked.Read(ref lastDiscordPublication))}",
        $"Last acknowledged: {DiscordDiagnosticTime(Interlocked.Read(ref lastDiscordAcknowledgement))}",
        $"Screen: {recognizedScreen?.ToString() ?? "none"}; scan: {lastScreenScan?.ToString() ?? "none"}; scan ms: {lastScreenScanMs:F0}",
        $"Screen scan at: {lastScreenScanAt:O}; {ScreenDiagnostic()}",
        $"Artwork cache: {previewImages.Count}; bytes: {PreviewCacheBytes}; fallbacks: {artworkFallbacks}",
        $"Visible layout findings: {UiLayoutAudit.Inspect(this).Count}",
        $"Working set MiB: {Environment.WorkingSet / 1048576d:F1}",
        "Navigation: handler + time until UI message queue is idle (not display latency). Cold budget 500 ms; warm budget 100 ms.",
        uiPerformance.Report(),
        "Local report only. No account names, paths, route text, coordinates or screenshots included.");

    private void BuildHelpPage()
    {
        var page = new LibraryPage(L("Ayuda y diagnóstico", "Help and diagnostics"));
        page.Size = pageHost.ClientSize;
        using var construction = new UiLayoutBatch(page);
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabStop = false };
        var content = Grid(1);
        content.Padding = new Padding(24);
        var connection = TextLabel("", 11);
        var detector = TextLabel("", 10);
        var measurements = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Top, Height = 192, AccessibleName = L("Mediciones locales", "Local measurements") };
        AddRows(content, TextLabel(page.Text, 20, true), connection, detector);
        var actions = ToolBar();
        void Action(string es, string en, Action run)
        {
            var button = MakeButton(L(es, en));
            button.Click += (_, _) => run();
            actions.Controls.Add(button);
        }
        void UpdateStatus()
        {
            connection.Text = gameStatus.Text + "\n" + discordStatus.Text + "\n" + guidance.Text;
            detector.Text = ScreenDiagnostic() + "\n" + L($"Última lectura: {lastScreenScanAt?.LocalDateTime.ToString("T") ?? "—"} · {lastScreenScanMs:F0} ms",
                $"Last scan: {lastScreenScanAt?.LocalDateTime.ToString("T") ?? "—"} · {lastScreenScanMs:F0} ms");
        }
        void Refresh()
        {
            UpdateStatus();
            measurements.Text = BuildDiagnosticReport();
        }
        var statusRefresh = new System.Windows.Forms.Timer { Interval = 2000 };
        statusRefresh.Tick += (_, _) => UpdateStatus();
        page.VisibleChanged += (_, _) => { if (page.Visible) statusRefresh.Start(); else statusRefresh.Stop(); };
        page.Disposed += (_, _) => statusRefresh.Dispose();
        Action("Comprobar ahora", "Check now", () => { RefreshPresence(); Refresh(); });
        Action("Abrir Discord", "Open Discord", () => OpenExternal("discord://"));
        Action("Abrir ETS2", "Launch ETS2", () => OpenExternal("steam://rungameid/227300"));
        Action("Republicar actividad", "Republish activity", () =>
        {
            Interlocked.Increment(ref connectionGeneration);
            publishImmediately = true;
            RefreshPresence();
            Refresh();
        });
        AddRows(content, actions, TextLabel(L("Rendimiento y conexión", "Performance and connection"), 12, true),
            TextLabel(L("Las mediciones permanecen en tu equipo. Puedes revisar el informe antes de guardarlo.",
                "Measurements stay on your computer. Review the report before saving it."), 10), measurements);
        AddAction(content, L("Revisar y exportar diagnóstico…", "Review and export diagnostics…"), () => ExportDiagnostics(this));
        AddRows(content, TextLabel(L("Portadas de Discord", "Discord artwork"), 12, true));
        var country = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top,
            AccessibleName = L("País de la portada", "Artwork country"), DisplayMember = nameof(ArtworkCountry.Name) };
        country.Items.Add(new ArtworkCountry(null, L("Genérica · Sin ubicación", "Generic · No location")));
        country.Items.AddRange(Countries.Codes.Select(code => new ArtworkCountry(code, Countries.Name(code, English)!))
            .OrderBy(item => item.Name).Cast<object>().ToArray());
        country.SelectedIndex = 0;
        var coach = new CheckBox { AutoSize = true, Text = L("Mostrar autobús", "Show coach") };
        var art = new PictureBox { Size = new Size(256, 256), SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false, AccessibleName = L("Vista previa local de portada", "Local artwork preview") };
        var note = TextLabel(L("Vista previa local. No modifica lo que estás publicando.", "Local preview. Does not change your published activity."), 10);
        void Preview()
        {
            var image = DiscordIdentity.ImageFor((country.SelectedItem as ArtworkCountry)?.Code, english: preferences.DiscordLanguage == "en", coach: coach.Checked);
            var previous = art.Image;
            art.Image = LoadPreviewImage(image) ?? LoadPreviewImage(DiscordIdentity.ImageFor(null, coach: coach.Checked));
            previous?.Dispose();
        }
        country.SelectedIndexChanged += (_, _) => Preview();
        coach.CheckedChanged += (_, _) => Preview();
        AddRows(content, country, coach, note, art);
        AddUpdateControls(content);
        viewport.Controls.Add(content);
        page.Controls.Add(viewport);
        void Wrap()
        {
            var width = viewport.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            foreach (var label in content.Controls.OfType<Label>()) SetWrap(label, width);
        }
        page.ClientSizeChanged += (_, _) => Wrap();
        page.Activated = Refresh;
        page.Disposed += (_, _) => art.Image?.Dispose();
        StyleTool(page);
        construction.Dispose();
        page.PerformLayout();
        PushLibraryPage(page);
        Wrap();
        Preview();
        Refresh();
    }

    private sealed record ArtworkCountry(string? Code, string Name);

    private void AddUpdateControls(TableLayoutPanel content)
    {
        AddRows(content, TextLabel(L("Actualizaciones", "Updates"), 12, true));
        var status = TextLabel("", 10);
        var bar = ToolBar();
        var check = MakeButton(L("Buscar actualización", "Check for updates"));
        var install = MakeButton(L("Descargar actualización", "Download update"));
        var rollback = MakeButton(L("Recuperar versión anterior", "Restore previous version"));
        void Render()
        {
            status.Text = availableUpdate is { } update ? L($"Disponible v{update.Version} · Actual v{DesktopServices.Version.ToString(3)}",
                $"Available v{update.Version} · Current v{DesktopServices.Version.ToString(3)}")
                : updateCheckFailed ? L("No se pudo consultar GitHub. Puedes reintentar.", "Could not reach GitHub. You can retry.")
                : updateCheckedAt is null ? L("Busca una versión nueva cuando quieras.", "Check for a new version whenever you like.")
                : L("No hay una versión más reciente en el canal elegido.", "No newer release in the selected channel.");
            install.Enabled = availableUpdate is not null && !updating;
            rollback.Enabled = File.Exists(AppUpdater.Previous(Application.ExecutablePath)) && !updating;
        }
        check.Click += async (_, _) =>
        {
            check.Enabled = false;
            status.Text = L("Consultando versiones en GitHub…", "Checking releases on GitHub…");
            await RefreshUpdateAsync();
            if (check.IsDisposed) return;
            check.Enabled = true;
            Render();
        };
        install.Click += (_, _) => ShowUpdate();
        rollback.Click += (_, _) => RollBackUpdate();
        bar.Controls.AddRange([check, install, rollback]);
        AddRows(content, status, bar);
        content.VisibleChanged += (_, _) => { if (content.Visible) Render(); };
        Render();
    }
}
