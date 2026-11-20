namespace Ets2DiscordPresence;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test") || args.Contains("--diagnostics-json"))
        {
            Environment.ExitCode = StartupDiagnostics.Run(args);
            return;
        }
        if (AppUpdater.HandleWorker(args)) return;
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ETS2DiscordPresence.Show");
        using var instance = new Mutex(true, @"Local\ETS2DiscordPresence.Instance", out var first);
        if (!first)
        {
            signal.Set();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var font = new Font("Segoe UI", 9);
        Application.SetDefaultFont(font);
        try { _ = LocalData.RecoverInterruptedRestore(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show("No se pudo recuperar la restauración de los datos de ETS2. Revisa la carpeta de copias y vuelve a abrir la app.\n" + ex.Message,
                "ETS2 Discord Presence", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.SetColorMode(AppSettings.Load().Theme switch
        {
            "day" => SystemColorMode.Classic,
            "night" when OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) => SystemColorMode.Dark,
            _ => SystemColorMode.System
        });
        using var window = new MainForm(args.Contains("--minimized"));
        _ = window.Handle;
        var activation = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
        {
            if (!window.IsDisposed && window.IsHandleCreated)
            {
                try { window.BeginInvoke(window.RestoreWindow); }
                catch (InvalidOperationException) { }
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
        window.Shown += (_, _) => window.BeginInvoke(() => AppUpdater.ConfirmStartup(args));
        try { Application.Run(window); }
        finally { activation.Unregister(null); }
    }
}
