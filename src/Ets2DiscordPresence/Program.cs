namespace Ets2DiscordPresence;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ETS2DiscordPresence.Show");
        using var instance = new Mutex(true, @"Local\ETS2DiscordPresence.Instance", out var first);
        if (!first)
        {
            signal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
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
        try { Application.Run(window); }
        finally { activation.Unregister(null); }
    }
}
