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
            MessageBox.Show(owner, L("Cierra y vuelve a abrir la app para cambiar de carpeta.", "Close and reopen the app to change folders."), Text);
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
        if (DataAction(owner, () => LocalData.Backup(picker.FileName, includeRecordings: include == DialogResult.Yes, active: recordingPath)))
            MessageBox.Show(owner, L("Copia guardada.", "Backup saved."), Text);
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
        if (picker.ShowDialog(owner) != DialogResult.OK || !Confirm(owner,
            L("Los ajustes, diarios y catálogos incluidos en la copia reemplazarán sus archivos locales. ¿Restaurar?",
                "Settings, journals and catalogs included in the backup will replace their local files. Restore?"))) return;
        journal.Flush();
        if (!DataAction(owner, () => LocalData.Restore(picker.FileName))) return;
        preferences = AppSettings.Load();
        journal = new TripJournal(LocalData.JournalPath(preferences.DriverProfile));
        completedTrip = null;
        privateDestination = true;
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
        MessageBox.Show(owner, L("Copia restaurada. Si cambió el tema, vuelve a abrir la app.", "Backup restored. If the theme changed, reopen the app."), Text);
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
