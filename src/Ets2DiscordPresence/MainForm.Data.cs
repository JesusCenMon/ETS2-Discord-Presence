using System.Diagnostics;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ChangeStorage(IWin32Window owner)
    {
        var enable = !LocalData.IsPortable;
        var choice = MessageBox.Show(owner, L("¿Copiar ahora ajustes, diarios y catálogos a la carpeta que se usará al reiniciar? Sí copia los datos y reemplaza los archivos del mismo nombre en el destino. No cambia de carpeta sin copiarlos.",
            "Copy settings, journals and catalogs to the folder used after restarting? Yes copies the data and replaces files with matching names at the destination. No switches folders without copying."),
            Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;
        journal.Flush();
        if (journal.Error is not null)
        {
            MessageBox.Show(owner, L("No se pudo guardar el diario.", "Could not save the journal."), Text);
            return;
        }
        if (DataAction(owner, () => LocalData.SetPortable(enable, choice == DialogResult.Yes)))
        {
            portableRestartPending = true;
            if (Confirm(owner, L("Modo portátil · Requiere reiniciar. ¿Reiniciar aplicación ahora?",
                "Portable mode · Requires restart. Restart the application now?"))) RestartApplication(owner);
        }
    }

    private void ExportBackup(IWin32Window owner)
    {
        using var picker = new SaveFileDialog
        {
            Filter = L("Copia de ETS2 (*.ets2backup)|*.ets2backup", "ETS2 backup (*.ets2backup)|*.ets2backup"),
            FileName = $"ETS2-{DateTime.Today:yyyy-MM-dd}.ets2backup"
        };
        if (picker.ShowDialog(owner) != DialogResult.OK) return;
        journal.Flush();
        if (journal.Error == "journal.write")
        {
            MessageBox.Show(owner, L("No se pudo guardar el diario antes de crear la copia.", "Could not save the journal before creating the backup."), Text);
            return;
        }
        long bytes = 0;
        if (!DataAction(owner, () => bytes = LocalData.RecordingBytes(recordingPath))) return;
        var include = bytes > 0 ? MessageBox.Show(owner,
            L($"¿Incluir las repeticiones guardadas? Tamaño estimado adicional: {bytes * 1.34 / 1_000_000:N1} MB.",
                $"Include saved replays? Estimated additional size: {bytes * 1.34 / 1_000_000:N1} MB."),
            Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) : DialogResult.No;
        if (include == DialogResult.Cancel) return;
        if (DataAction(owner, () => SaveBackup(picker.FileName, include == DialogResult.Yes)))
            MessageBox.Show(owner, L("Copia guardada.", "Backup saved."), Text);
    }

    private static string BackupStatusFile => Path.Combine(LocalData.Folder, "last-backup.txt");

    private void SaveBackup(string file, bool includeRecordings = false)
    {
        LocalData.Backup(file, includeRecordings: includeRecordings, active: recordingPath);
        LocalData.WriteFile(BackupStatusFile, DateTimeOffset.UtcNow.ToString("O"));
    }

    private DateTimeOffset? LastBackup()
    {
        try { return File.Exists(BackupStatusFile) && DateTimeOffset.TryParse(File.ReadAllText(BackupStatusFile), out var date) ? date : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void InspectBackup(IWin32Window owner)
    {
        using var picker = new OpenFileDialog { Filter = "ETS2 (*.ets2backup)|*.ets2backup" };
        if (picker.ShowDialog(owner) != DialogResult.OK) return;
        DataAction(owner, () => MessageBox.Show(owner, BackupDescription(LocalData.InspectBackup(picker.FileName)), Text));
    }

    private string BackupDescription(LocalData.BackupInfo info) =>
        $"{info.Created.LocalDateTime:g} · v{info.Version}\n"
        + L($"Diarios {info.Counts.GetValueOrDefault("trips")} · Catálogos {info.Counts.GetValueOrDefault("catalog")} · Repeticiones {info.Counts.GetValueOrDefault("recording")}",
            $"Journals {info.Counts.GetValueOrDefault("trips")} · Catalogs {info.Counts.GetValueOrDefault("catalog")} · Replays {info.Counts.GetValueOrDefault("recording")}")
        + (info.RemovalCount == 0 ? "" : L($"\nAl deshacer, se eliminarán hasta {info.RemovalCount} archivos añadidos por la restauración.",
            $"Undo can remove up to {info.RemovalCount} files added by the restore."));

    private void ShowStorage(IWin32Window owner)
    {
        using var window = ToolWindow(L("Almacenamiento local", "Local storage"), new Size(560, 330));
        var rows = Grid(1);
        rows.Padding = new Padding(12);
        var usage = TextLabel("", 11);
        var cleanup = MakeButton(L("Liberar espacio…", "Free up space…"));
        var location = new TextBox { ReadOnly = true, Dock = DockStyle.Top, Text = LocalData.Folder,
            AccessibleName = L("Ruta de almacenamiento", "Storage path") };
        var copyPath = MakeButton(L("Copiar ruta", "Copy path"));
        copyPath.Anchor = AnchorStyles.Left;
        copyPath.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(location.Text);
                copyPath.Text = L("Ruta copiada", "Path copied");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                location.SelectAll();
                location.Focus();
            }
        };
        AddRows(rows, TextLabel(location.AccessibleName, 10, true), location, copyPath, usage, cleanup);
        window.Controls.Add(rows);
        static IEnumerable<string> Files(string? directory, string pattern) => !string.IsNullOrWhiteSpace(directory)
            && Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern) : [];
        static string SizeOf(IEnumerable<string> files) => $"{files.Sum(file => new FileInfo(file).Length) / 1_000_000d:N2} MB";
        void Refresh()
        {
            var active = recordingPath is { } path && File.Exists(path) ? new[] { path } : [];
            usage.Text = $"{L("Grabación activa", "Active recording")} · {SizeOf(active)}\n"
                + $"{L("Grabaciones cerradas", "Saved recordings")} · {SizeOf(LocalData.RecordingFiles().Except(active, StringComparer.OrdinalIgnoreCase))}\n"
                + $"{L("Papelera de grabaciones", "Recording trash")} · {SizeOf(Files(LocalData.RecordingTrash, "ETS2-*.jsonl*"))}\n"
                + $"{L("Diarios", "Journals")} · {SizeOf(Files(LocalData.Folder, "trips*.json"))}\n"
                + $"{L("Catálogos", "Catalogs")} · {SizeOf(Files(Path.Combine(LocalData.Folder, "catalogs"), "*.json"))}\n"
                + $"{L("Copias en carpetas configuradas", "Backups in configured folders")} · "
                + SizeOf(Files(Path.Combine(LocalData.Folder, "backups"), "*.ets2backup")
                    .Concat(Files(preferences.Features.BackupFolder, "*.ets2backup")).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        cleanup.Click += (_, _) => DataAction(window, () =>
        {
            var old = LocalData.OldRecordings(preferences.RetentionDays, recordingPath);
            var expired = LocalData.ExpiredTrash();
            var temporary = LocalData.OldRecordingTemporaryFiles();
            if (old.Length + expired.Length + temporary.Length == 0)
            {
                MessageBox.Show(window, L("No hay archivos que cumplan los criterios de limpieza.", "No files meet the cleanup criteria."), Text);
                return;
            }
            if (!Confirm(window, L(
                $"Se moverán {old.Length} grabaciones antiguas a la papelera. Se borrarán {expired.Length} archivos vencidos y {temporary.Length} temporales ({SizeOf(expired.Concat(temporary))}).",
                $"{old.Length} old recordings will move to trash. {expired.Length} expired files and {temporary.Length} temporary files will be deleted ({SizeOf(expired.Concat(temporary))})."))) return;
            foreach (var file in old) LocalData.TrashRecording(file);
            foreach (var file in expired.Concat(temporary)) File.Delete(file);
            Refresh();
        });
        if (!DataAction(window, Refresh)) return;
        StyleTool(window);
        window.ShowDialog(owner);
    }

    private void RestoreBackup(Form owner)
    {
        if (liveDelivery || recording is not null)
        {
            MessageBox.Show(owner, L("Termina la entrega y detén la grabación antes de restaurar una copia.",
                "Finish the delivery and stop recording before restoring a backup."), Text);
            return;
        }
        using var picker = new OpenFileDialog
        {
            Filter = L("Copia de ETS2 (*.ets2backup)|*.ets2backup", "ETS2 backup (*.ets2backup)|*.ets2backup")
        };
        if (picker.ShowDialog(owner) != DialogResult.OK) return;
        LocalData.BackupInfo? info = null;
        if (!DataAction(owner, () => info = LocalData.InspectBackup(picker.FileName)) || info is null) return;
        using var choose = ToolWindow(L("Restaurar categorías", "Restore categories"), new Size(465, 300));
        var choices = Grid(1);
        choices.Padding = new Padding(12);
        AddRows(choices, TextLabel(BackupDescription(info), 10));
        var categories = new[] { ("settings", L("Ajustes", "Settings")), ("trips", L("Diarios", "Journals")),
            ("catalog", L("Catálogos", "Catalogs")), ("recording", L("Repeticiones", "Replays")) };
        var checks = categories.Select(item => Check(choices, item.Item2, info.Counts.ContainsKey(item.Item1))).ToArray();
        for (var i = 0; i < checks.Length; i++) checks[i].Enabled = info.Counts.ContainsKey(categories[i].Item1);
        var accept = MakeButton(L("Restaurar selección", "Restore selection"));
        accept.DialogResult = DialogResult.OK;
        AddRows(choices, accept);
        choose.Controls.Add(choices);
        StyleTool(choose);
        if (choose.ShowDialog(owner) != DialogResult.OK) return;
        var selected = categories.Where((_, index) => checks[index].Checked).Select(item => item.Item1).ToHashSet();
        if (selected.Count == 0) return;
        journal.Flush();
        if (journal.Error == "journal.write") return;
        var snapshot = Path.Combine(LocalData.Folder, "backups", $"Antes-de-restaurar-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.ets2backup");
        if (!DataAction(owner, () => LocalData.Restore(picker.FileName, categories: selected, snapshot: snapshot))) return;
        preferences = AppSettings.Load();
        journal = new TripJournal(LocalData.JournalPath(preferences.DriverProfile));
        completedTrip = null;
        privateDestination = true;
        publishImmediately = true;
        LoadCatalog();
        gamePrepared = false;
        setupError = null;
        nextGameCheck = 0;
        ApplyShellTheme();
        ApplyLanguage();
        RegisterPrivacyShortcut();
        ScaleText(this);
        ConfigurePanels();
        RefreshPresence();
        MessageBox.Show(owner, L("Copia restaurada. Puedes recuperar los datos anteriores restaurando esta copia:",
            "Backup restored. You can recover the previous data by restoring this backup:") + $"\n\n{snapshot}", Text);
        owner.Close();
    }

    private void ShowReader(IWin32Window owner)
    {
        using var window = ToolWindow(L("Lector de ETS2", "ETS2 reader"), new Size(650, 370));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var layout = Grid(1);
        layout.Padding = new Padding(16);
        var path = gamePath;
        var gameVersion = SteamLocator.IsValidGamePath(path)
            ? FileVersionInfo.GetVersionInfo(Path.Combine(path!, "bin", "win_x64", "eurotrucks2.exe")).FileVersion : null;
        var status = TextLabel(lastRevision is { } revision
            ? revision == TelemetryReader.ExpectedRevision
                ? L($"Datos compatibles · Revisión {revision}", $"Compatible data · Revision {revision}")
                : L($"Revisión {revision} incompatible. Cierra ETS2 y revisa su carpeta de complementos.", $"Revision {revision} is incompatible. Close ETS2 and check its plugins folder.")
            : readerError ?? L("Entra al camión para comprobar los datos del lector.", "Enter the truck to verify reader data."), 10);
        var updateStatus = TextLabel(L("Consulta la versión disponible del lector.", "Check the available reader version."), 10);
        void Wrap()
        {
            SetWrap(status, window.ClientSize.Width - 48);
            SetWrap(updateStatus, window.ClientSize.Width - 48);
        }
        window.Resize += (_, _) => Wrap();
        Wrap();
        var check = MakeButton(L("Buscar actualización del lector", "Check reader update"));
        var release = MakeButton(L("Ver versiones del lector", "View reader releases"));
        release.Click += (_, _) => OpenExternal("https://github.com/RenCloud/scs-sdk-plugin/releases");
        check.Click += async (_, _) =>
        {
            check.Enabled = false;
            try
            {
                var version = await DesktopServices.CheckReaderVersionAsync(cancellation.Token);
                if (window.IsDisposed) return;
                updateStatus.Text = version > Version.Parse(PluginInstaller.BundledVersion)
                    ? L($"Disponible {version}. Consulta sus cambios antes de instalarla.", $"Version {version} is available. Review its changes before installing.")
                    : L("Ya se incluye la versión más reciente del lector.", "The latest reader version is already bundled.");
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or FormatException)
            {
                if (!window.IsDisposed) updateStatus.Text = L("No se pudo consultar la versión disponible.", "Could not check the available version.");
            }
            finally { if (!window.IsDisposed) check.Enabled = true; }
        };
        window.FormClosed += (_, _) => cancellation.Cancel();
        AddRows(layout, TextLabel($"RenCloud {PluginInstaller.BundledVersion}", 16, true),
            TextLabel($"ETS2 · {gameVersion ?? L("Versión sin detectar", "Version undetected")}", 11), status, updateStatus, check, release);
        window.Controls.Add(layout);
        StyleTool(window);
        window.ShowDialog(owner);
    }
}
