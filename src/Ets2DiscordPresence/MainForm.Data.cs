using System.Diagnostics;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private void ChangeStorage(IWin32Window owner)
    {
        var enable = !LocalData.IsPortable;
        if (!Confirm(owner, enable
            ? L("La próxima vez, la app usará la carpeta ETS2PresenceData junto al ejecutable. Puedes llevar tus datos con una copia de seguridad. ¿Activar?",
                "Next launch will use the ETS2PresenceData folder beside the executable. You can transfer your data with a backup. Enable?")
            : L("La próxima vez, la app volverá a usar tu perfil de Windows. ¿Continuar?",
                "Next launch will use your Windows profile again. Continue?"))) return;
        if (DataAction(owner, () => LocalData.SetPortable(enable)))
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
        if (DataAction(owner, () => LocalData.Backup(picker.FileName)))
            MessageBox.Show(owner, L("Copia guardada. Incluye ajustes, diarios y catálogos.", "Backup saved. It includes settings, journals and catalogs."), Text);
    }

    private void RestoreBackup(Form owner)
    {
        if (liveDelivery)
        {
            MessageBox.Show(owner, L("Termina la entrega antes de restaurar una copia.", "Finish the delivery before restoring a backup."), Text);
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
