namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, Button> pinnedActions = [];
    private readonly Label driveSummary = TextLabel("", 21, true);
    private readonly Label previewMetadata = TextLabel("", 9);
    private readonly FlowLayoutPanel previewArt = ToolBar();
    private readonly PictureBox previewLarge = new() { Size = new Size(120, 120), SizeMode = PictureBoxSizeMode.Zoom };
    private string? previewImageKey;
    private bool updating;
    private volatile PresenceText? lastSent;
    private volatile bool discordConfirmed;
    private TelemetrySnapshot? liveSnapshot;
    private bool gameRunning;
    private DateTime? sessionStarted;
    private bool previouslyConnected;
    private bool connectedBefore;
    private bool restAlerted;
    private DateTimeOffset? quietUntil;
    private DateTimeOffset? privateUntil;
    private bool privateForSession;
    private double? lastRemaining;
    private float? lastEta;
    private string? lastRouteKey;
    private string? demoScenario;
    private BrowserOverlay? overlay;

    private string ActionName(string key) => key switch
    {
        "record" => L("Grabar", "Record"),
        "privacy" => L("Privacidad", "Privacy"),
        _ => L("Tarjeta OBS", "OBS card")
    };

    private void AddDashboardActions(FlowLayoutPanel actions)
    {
        foreach (var key in new[] { "record", "privacy", "obs" })
        {
            var button = MakeButton(ActionName(key));
            pinnedActions.Add(key, button);
            button.Click += (_, _) =>
            {
                if (key == "record") ToggleRecording(this);
                else if (key == "privacy") ShowPrivacyMenu(button);
                else ShowCaptureCard();
            };
            actions.Controls.Add(button);
        }
        var fix = Translate(MakeButton(""), "Revisar conexión", "Check connection");
        fix.Click += (_, _) => ShowConnectionGuide();
        actions.Controls.Add(fix);
        driveSummary.Padding = new Padding(12);
    }

    private readonly Label tripStatus = TextLabel("", 10);
    private double? etaDeltaMinutes;
    private bool routeRecalculated;
    private int stableRouteSamples;

    private void RenderTripMeasurements(TelemetrySnapshot? trip, bool isLive, long now)
    {
        var values = new List<string>();
        var measured = isLive && trip?.OnJob == true ? journal.LiveMeasurements : null;
        if (trip?.Signals is { Speed: >= -100 and <= 100 } signals)
            values.Add(L($"Velocidad {SpeedLabel(Math.Abs(signals.Speed) * 3.6).PadLeft(8, '\u2007')}", $"Speed {SpeedLabel(Math.Abs(signals.Speed) * 3.6).PadLeft(8, '\u2007')}"));
        if (measured?.AverageKph is { } average) values.Add(L($"Media {SpeedLabel(average)}", $"Avg. {SpeedLabel(average)}"));
        if (measured is { MaximumKph: not null })
        {
            driveTime.Text = L($"Al volante  {Duration(measured.MovingSeconds)}", $"Driving  {Duration(measured.MovingSeconds)}");
            stoppedTime.Text = L($"Detenido  {Duration(measured.StoppedSeconds)}", $"Stopped  {Duration(measured.StoppedSeconds)}");
        }
        tripTimes.Visible = measured?.MaximumKph is not null && preferences.Features.Layout != "drive"
            && preferences.HiddenPanels?.Contains("metrics") != true;
        arrivalNotice.Visible = false;
        if (trip?.Progress is { } percent) values.Add(L($"Progreso {percent}%", $"Progress {percent}%"));
        if (isLive && trip?.OnJob == true)
        {
            if (routeChangedUntil > now)
            {
                arrivalNotice.Text = L("Ruta recalculada", "Route recalculated")
                    + (etaDeltaMinutes is { } delta && Math.Abs(delta) >= 1 ? $" · {delta:+0;-0} min" : "");
                arrivalNotice.Visible = true;
            }
            else
                values.Add(trip.ArrivalTime is not null && stableRouteSamples >= 3
                    ? L("ETA estable", "ETA stable") : L("ETA incierta", "ETA uncertain"));
        }
        tripStatus.Visible = values.Count > 0 && preferences.HiddenPanels?.Contains("metrics") != true;
        tripStatus.Text = values.Count == 0 ? "" : L("Estado del viaje", "Trip status") + "\n" + string.Join("  ·  ", values);
    }

    private string SpeedLabel(double kph) => preferences.Features.Miles ? $"{kph / 1.609344:N0} mph" : $"{kph:N0} km/h";
    private static string Duration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 100_000_000));
        return $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";
    }

    private void RenderDashboard(TelemetrySnapshot? displayed, bool running, bool connected, long now)
    {
        var options = preferences.Features;
        var driving = options.Layout == "drive";
        var streaming = options.Layout == "stream";
        travel.Visible = driving || streaming || preferences.HiddenPanels?.Contains("trip") != true;
        driveSummary.Visible = driving;
        foreach (Control control in travel.Controls)
        {
            if (control == driveSummary) continue;
            if (driving) control.Visible = false;
        }
        if (!driving)
        {
            travelMode.Visible = true;
            if (truckName.Parent is { } truckCard) truckCard.Visible = true;
        }
        var km = displayed?.RemainingKm is { } remaining ? AppOptions.Distance(remaining, options.Miles) : "—";
        var next = displayed?.OnJob == true && !string.IsNullOrWhiteSpace(displayed.DestinationCity)
            ? displayed.DestinationCity : city ?? Countries.Name(currentCountry, English) ?? L("Sin destino", "No destination");
        var eta = displayed?.ArrivalTime is { } time ? $"  ·  {L("Llegada", "Arrival")} {time}" : "";
        driveSummary.Text = $"{next}  ·  {km}{eta}\nDiscord {(connected ? L("conectado", "connected") : L("sin conexión", "offline"))}";
        RenderTripMeasurements(displayed, !demo && demoScenario is null, now);
        if (driving)
        {
            if (tripStatus.Visible) driveSummary.Text += "\n" + tripStatus.Text;
            tripStatus.Visible = false;
        }
        if (driving) mapSection.Visible = false;
        else mapSection.Visible = preferences.HiddenPanels?.Contains("map") != true && (!preferences.Compact || fullScreen);
        if (driving) preview.Visible = false;
        else preview.Visible = (streaming || preferences.HiddenPanels?.Contains("discord") != true) && (!preferences.Compact || streaming) && !fullScreen;
        if (streaming || driving) artwork.Visible = false;
        foreach (var (key, button) in pinnedActions)
        {
            button.Visible = options.Pinned?.Contains(key) == true || streaming && key == "obs";
            button.Text = key == "record" && recording is not null ? L("Detener grabación", "Stop recording") : ActionName(key);
        }
        travelMode.ForeColor = options.HighContrast ? SystemColors.HighlightText : displayed is { Paused: false } ? shellPalette.Accent : shellPalette.Text;
        travelMode.BackColor = options.HighContrast ? SystemColors.Highlight : Color.Transparent;
        timer.Interval = options.LowPower && !Visible && displayed?.OnJob != true && recording is null && !options.AutoRecord ? 6000 : 2000;
    }

    private void ConfigurePreviewArt()
    {
        previewLarge.AccessibleName = L("Imagen de la actividad", "Activity artwork");
        previewArt.Controls.Add(previewLarge);
        Disposed += (_, _) => previewLarge.Image?.Dispose();
    }

    private static readonly HttpClient PreviewClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private async Task UpdateLargePreviewAsync(string image)
    {
        try
        {
            using var response = await PreviewClient.GetAsync(image, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 4_000_000) return;
            await using var input = await response.Content.ReadAsStreamAsync(lifetime.Token);
            using var buffer = new MemoryStream();
            var block = new byte[16_384];
            int read;
            while ((read = await input.ReadAsync(block, lifetime.Token)) > 0)
            {
                if (buffer.Length + read > 4_000_000) return;
                buffer.Write(block, 0, read);
            }
            buffer.Position = 0;
            if (IsDisposed || previewImageKey != image) return;
            using var decoded = Image.FromStream(buffer);
            if (decoded.Width > 4096 || decoded.Height > 4096) return;
            previewLarge.Image?.Dispose();
            previewLarge.Image = new Bitmap(decoded);
            previewArt.Visible = true;
            SyncHomeArt();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ArgumentException) { }
    }

    private static Image? LoadPreviewImage(string? image)
    {
        if (image is null) return null;
        var file = Path.GetFileName(image);
        var resource = DiscordIdentity.CountryArtworkResource(image);
        if (resource is null && file != "hero-truck.png") return null;
        using var input = AppResources.Open(resource ?? file);
        using var picture = Image.FromStream(input);
        return new Bitmap(picture);
    }

    private void RenderPreview(PresenceText? outgoing, PresenceText? sample)
    {
        var shown = outgoing ?? (demo || demoScenario is not null ? sample : null);
        var elapsed = shown?.Started is { } start ? DateTime.UtcNow - start : TimeSpan.Zero;
        var age = shown?.Started is null ? null : $"{Math.Max(0, (int)elapsed.TotalHours):00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        previewMetadata.Text = shown is null ? L("Sin actividad enviada", "No activity sent")
            : string.Join(" · ", new[] { shown.Image is null ? null : L("Imagen del juego activa", "Game artwork active"), age }.OfType<string>());
        if (previewImageKey != shown?.Image)
        {
            previewImageKey = shown?.Image;
            previewLarge.Image?.Dispose();
            previewLarge.Image = LoadPreviewImage(previewImageKey);
            SyncHomeArt();
            if (previewLarge.Image is null && previewImageKey is { } remote) _ = UpdateLargePreviewAsync(remote);
        }
        RenderHomePresence(shown, outgoing, age);
        previewArt.Visible = previewLarge.Image is not null;
        if (shown is null)
        {
            details.Text = L("Actividad en espera", "Activity on hold");
            state.Text = "";
            return;
        }
        details.Text = shown.Details;
        state.Text = shown.State;
    }

    private bool AlertsAllowed(TelemetrySnapshot? live) =>
        (quietUntil is null || DateTimeOffset.UtcNow >= quietUntil)
        && preferences.Features.AlertMode != "always"
        && (preferences.Features.AlertMode != "job" || live?.OnJob != true);

    private void UpdateLiveNotices(TelemetrySnapshot? live, bool connected)
    {
        var allowed = AlertsAllowed(live);
        if (connected && !previouslyConnected && connectedBefore && preferences.Features.ReconnectNotice && allowed)
            tray.ShowBalloonTip(3500, "Discord", L("Conexión recuperada.", "Connection restored."), ToolTipIcon.Info);
        previouslyConnected = connected;
        connectedBefore |= connected;
        var rest = live?.Signals?.RestMinutes;
        if (rest is null or < 0 or > 45) restAlerted = false;
        if (live is { Paused: false } && rest is > 0 and <= 30 && !restAlerted && preferences.Features.RestNotice && allowed)
        {
            restAlerted = true;
            tray.ShowBalloonTip(4000, L("Próximo descanso", "Rest stop due"),
                L($"Quedan {rest} minutos del juego.", $"{rest} in-game minutes remaining."), ToolTipIcon.Info);
        }
        var key = live is { OnJob: true } ? live.Signals?.JobKey : null;
        var remaining = live?.RemainingKm;
        var eta = live?.RouteSeconds;
        var distanceChanged = remaining is { } next && lastRemaining is { } previous
            && Math.Abs(next - previous) >= Math.Max(25, previous * .2);
        var etaChanged = eta is > 0 and < 604800 && lastEta is > 0 and < 604800
            && Math.Abs(eta.Value - lastEta.Value) >= Math.Max(1800, lastEta.Value * .35);
        routeRecalculated = live is { Paused: false } && key is not null && key == lastRouteKey
            && (distanceChanged || etaChanged);
        if (key is null || key != lastRouteKey || live is not { Paused: false })
        {
            routeChangedUntil = 0;
            stableRouteSamples = 0;
        }
        else if (routeRecalculated)
        {
            routeChangedUntil = Environment.TickCount64 + 12_000;
            etaDeltaMinutes = eta is > 0 and < 604800 && lastEta is > 0 and < 604800
                ? (eta.Value - lastEta.Value) / 60d : null;
            stableRouteSamples = 0;
        }
        else if (eta is > 0 and < 604800 && remaining is > 0) stableRouteSamples = Math.Min(3, stableRouteSamples + 1);
        else stableRouteSamples = 0;
        lastRouteKey = key;
        lastRemaining = remaining;
        lastEta = eta;
    }

    private long routeChangedUntil;
    private string? quotaWarning;
    private long lastDiscordConnection;
    private long lastDiscordPublication;
    private volatile string? lastDiscordError;
    private bool releaseArtwork;

    private void ShowPrivacyMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(DestinationPrivacyLabel(), null, (_, _) => ToggleDestinationPrivacy());
        menu.Items.Add(L("Ocultar toda la actividad 15 minutos", "Hide all activity for 15 minutes"), null, (_, _) => SetPrivacy(TimeSpan.FromMinutes(15)));
        menu.Items.Add(L("Ocultar toda la actividad una hora", "Hide all activity for one hour"), null, (_, _) => SetPrivacy(TimeSpan.FromHours(1)));
        menu.Items.Add(L("Ocultar toda la actividad hasta cerrar ETS2", "Hide all activity until ETS2 closes"), null, (_, _) =>
        {
            privateForSession = true;
            RefreshPresence();
        });
        menu.Items.Add(L("Terminar pausa temporal", "End temporary pause"), null, (_, _) =>
        {
            privateUntil = null;
            privateForSession = false;
            RefreshPresence();
        });
        menu.Items.Add(L("Silenciar avisos una hora", "Mute notifications for one hour"), null, (_, _) => quietUntil = DateTimeOffset.UtcNow.AddHours(1));
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void CopyObsUrl(IWin32Window owner)
    {
        if (!preferences.Features.ObsBrowser)
            SavePreferences(preferences with { Options = preferences.Features with { ObsBrowser = true } });
        try
        {
            StartOverlay();
            if (overlay is null) return;
            Clipboard.SetText(overlay.Url);
            MessageBox.Show(owner, L("Dirección copiada. En OBS, pégala en una fuente de navegador.",
                "URL copied. Paste it into an OBS browser source.") + $"\n\n{overlay.Url}", Text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Sockets.SocketException
            or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(owner, L("No se pudo iniciar o copiar la fuente de OBS.", "Could not start or copy the OBS source."), Text);
        }
    }

    private void RegenerateObsUrl(IWin32Window owner)
    {
        if (!Confirm(owner, L("La dirección anterior dejará de funcionar. ¿Generar una nueva?",
            "The old URL will stop working. Generate a new one?"))) return;
        try
        {
            var key = Guid.NewGuid().ToString("N");
            LocalData.WriteFile(Path.Combine(LocalData.Folder, "obs-key.txt"), key);
            overlay?.Rotate(key);
            CopyObsUrl(owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, L("No se pudo regenerar la dirección.", "Could not regenerate the URL."), Text);
        }
    }

    private void StartOverlay()
    {
        if (!preferences.Features.ObsBrowser || overlay is not null) return;
        var file = Path.Combine(LocalData.Folder, "obs-key.txt");
        var key = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        if (key.Length != 32 || !key.All(Uri.IsHexDigit))
        {
            key = Guid.NewGuid().ToString("N");
            LocalData.WriteFile(file, key);
        }
        overlay = new BrowserOverlay(key);
    }

    private void SetPrivacy(TimeSpan duration)
    {
        privateUntil = DateTimeOffset.UtcNow + duration;
        RefreshPresence();
    }

    private void ShowConnectionGuide()
    {
        using var window = ToolWindow(L("Comprobar conexión", "Check connection"), new Size(580, 405));
        var content = Grid(1);
        content.Padding = new Padding(16);
        var steps = TextLabel("", 11, true);
        void Render()
        {
            var active = liveSnapshot is not null && discordReady && preferences.Sharing
                && !demo && demoScenario is null && !privateForSession && !(privateUntil > DateTimeOffset.UtcNow);
            steps.Text = $"1. {gameStatus.Text}\n2. {discordStatus.Text}\n3. "
                + (active && discordConfirmed ? L("Texto confirmado por Discord", "Text acknowledged by Discord")
                    : active ? L("Actividad enviada, esperando respuesta", "Activity sent, awaiting acknowledgement")
                    : L("Actividad pendiente", "Activity pending"))
                + $"\n\n{guidance.Text}";
        }
        AddRows(content, TextLabel(L("Conectar ETS2 con Discord", "Connect ETS2 to Discord"), 16, true), steps);
        AddAction(content, L("Abrir Discord", "Open Discord"), () => OpenExternal("discord://"));
        AddAction(content, L("Abrir ETS2", "Launch ETS2"), () => OpenExternal("steam://rungameid/227300"));
        AddAction(content, L("Comprobar ahora", "Check now"), () => { RefreshPresence(); Render(); });
        AddAction(content, L("Republicar actividad", "Republish activity"), () =>
        {
            Interlocked.Increment(ref connectionGeneration);
            publishImmediately = true;
            RefreshPresence();
            Render();
        });
        AddAction(content, L("Exportar diagnóstico…", "Export diagnostics…"), () => ExportDiagnostics(window));
        var artStatus = TextLabel("", 10);
        AddRows(content, artStatus);
        AddAction(content, L("Comprobar imágenes de Discord", "Check Discord artwork"), async () =>
        {
            artStatus.Text = L("Comprobando…", "Checking…");
            var visible = PresencePrivacy.Apply(preferences, liveSnapshot?.OnJob, privateDestination);
            var code = visible.SharesCountry ? currentCountry : null;
            var english = preferences.DiscordLanguage == "en";
            var images = new[] { DiscordIdentity.ImageFor(code, english: english),
                DiscordIdentity.ImageFor(code, english: english, coach: true) };
            var checks = await Task.WhenAll(images.Select(VerifyArtworkAsync));
            if (!window.IsDisposed) artStatus.Text = L($"Imágenes disponibles · {checks.Count(available => available)} de {images.Length}",
                $"Artwork available · {checks.Count(available => available)} of {images.Length}");
        });
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(content);
        window.Controls.Add(panel);
        SetWrap(steps, window.ClientSize.Width - 64);
        window.Resize += (_, _) => SetWrap(steps, window.ClientSize.Width - 64);
        Render();
        StyleTool(window);
        window.ShowDialog(this);
    }

    private void ExportDiagnostics(IWin32Window owner)
    {
        using var picker = new SaveFileDialog { Filter = "TXT (*.txt)|*.txt", FileName = "ETS2-diagnostico.txt" };
        if (picker.ShowDialog(owner) != DialogResult.OK) return;
        var version = SteamLocator.IsValidGamePath(gamePath)
            ? System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(gamePath!, "bin", "win_x64", "eurotrucks2.exe")).FileVersion : null;
        var report = string.Join(Environment.NewLine, $"ETS2 Discord Presence {DesktopServices.Version.ToString(3)}",
            $"OS {Environment.OSVersion.Version}", $"ETS2: {version ?? "—"}", $"Plugin API: {lastRevision?.ToString() ?? "—"}",
            gameStatus.Text, discordStatus.Text, $"Setup error: {setupError is not null}", $"Reader error: {readerError is not null}",
            $"Discord connection: {DiscordDiagnosticTime(Interlocked.Read(ref lastDiscordConnection))}",
            $"Discord publication: {DiscordDiagnosticTime(Interlocked.Read(ref lastDiscordPublication))}",
            $"Discord error: {lastDiscordError ?? "—"}");
        DataAction(owner, () => File.WriteAllText(picker.FileName, report));
    }

    private static string DiscordDiagnosticTime(long seconds) => seconds == 0 ? "—" : DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("O");

    private async Task CheckReleaseArtworkAsync()
    {
        if (!preferences.ActivityImage) return;
        var available = await VerifyArtworkAsync(DiscordIdentity.ActivityImage);
        if (!IsDisposed) releaseArtwork = available;
    }

    private async Task<bool> VerifyArtworkAsync(string uri)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await PreviewClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            return response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return false; }
    }

    private async void ShowUpdate()
    {
        if (updating || availableUpdate is not { } update) return;
        if (update.Sha256 is null) { OpenExternal(update.Url); return; }
        updating = true;
        var phase = "download";
        try
        {
            guidance.Text = L("Descargando la actualización…", "Downloading the update…");
            await AppUpdater.DownloadAsync(update, Application.ExecutablePath, lifetime.Token);
            if (IsDisposed || !Confirm(this, L("Actualización verificada. ¿Cerrar y volver a abrir la app para instalarla?",
                "Update verified. Close and reopen the app to install it?"))) return;
            phase = "swap";
            AppUpdater.Start(Application.ExecutablePath, rollback: false, update.Sha256);
            Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException
            or System.ComponentModel.Win32Exception)
        {
            AppUpdater.RecordFailure(Application.ExecutablePath, phase, ex);
            if (!IsDisposed) MessageBox.Show(this, L("No se pudo actualizar desde esta carpeta. La versión actual sigue disponible.",
                "Could not update from this folder. The current version is still available."), Text);
        }
        finally { updating = false; }
    }

    private void RollBackUpdate()
    {
        if (updating || !Confirm(this, L("¿Cerrar la app y recuperar el ejecutable anterior?",
            "Close the app and restore the previous executable?"))) return;
        try
        {
            AppUpdater.Start(Application.ExecutablePath, rollback: true, null);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, L("No se pudo abrir el actualizador.", "Could not start the updater."), Text);
        }
    }

    private void ShowSimulation()
    {
        using var window = ToolWindow(L("Prueba local", "Local test"), new Size(500, 360));
        var content = Grid(1);
        content.Padding = new Padding(16);
        AddRows(content, TextLabel(L("Prueba el panel sin publicar en Discord.", "Try the dashboard without publishing to Discord."), 12, true));
        foreach (var (key, label) in new[] { ("job", L("Entrega", "Delivery")), ("coach", L("Ruta de pasajeros", "Passenger route")), ("pause", L("Pausa", "Paused")),
            ("roam", L("Conducción libre en España", "Free driving in Spain")), ("offline", L("Desconexión", "Disconnected")) })
            AddAction(content, label, () => { demoScenario = key; RefreshPresence(); window.Close(); });
        AddAction(content, L("Salir de la prueba", "Exit test"), () => { demoScenario = null; demo = false; RefreshPresence(); window.Close(); });
        window.Controls.Add(content);
        StyleTool(window);
        window.ShowDialog(this);
    }
}
