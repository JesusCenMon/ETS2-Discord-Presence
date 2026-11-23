namespace Ets2DiscordPresence;

internal sealed class UiMotion(Func<bool> enabled) : IDisposable
{
    private static long nextSystemCheck;
    private static bool systemAnimations = true;
    internal static bool SystemAnimationsEnabled
    {
        get
        {
            var now = Environment.TickCount64;
            if (now >= nextSystemCheck)
            {
                // SPI_GETCLIENTAREAANIMATION: read Windows' motion preference, never modify it.
                // https://learn.microsoft.com/windows/win32/winauto/client-area-animation
                systemAnimations = !SystemParametersInfo(0x1042, 0, out var allowed, 0) || allowed;
                nextSystemCheck = now + 2000;
            }
            return systemAnimations;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] out bool enabled, uint flags);

    internal const int Fast = 125;
    internal const int Normal = 235;
    internal const int Slow = 320;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly Dictionary<object, Transition> transitions = new();
    private readonly List<KeyValuePair<object, Transition>> frame = [];
    private sealed record Transition(long Started, int Duration, Action<double> Update, Func<bool> Visible);

    public void Run(object key, Control owner, int duration, Action<double> update)
    {
        transitions.Remove(key);
        if (!enabled() || !owner.Visible || owner.IsDisposed)
        {
            update(1);
            return;
        }
        if (!timer.Enabled)
        {
            timer.Tick -= Tick;
            timer.Tick += Tick;
            timer.Start();
        }
        transitions[key] = new(Environment.TickCount64, duration, update, () => !owner.IsDisposed && owner.Visible);
        update(0);
    }

    public void Complete(object key)
    {
        if (transitions.Remove(key, out var transition)) transition.Update(1);
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        var animate = enabled();
        frame.Clear();
        frame.AddRange(transitions);
        foreach (var (key, transition) in frame)
        {
            if (!transitions.TryGetValue(key, out var current) || current != transition) continue;
            var fraction = animate && transition.Visible() ? Math.Clamp((double)(now - transition.Started) / transition.Duration, 0, 1) : 1;
            if (fraction == 1) transitions.Remove(key);
            transition.Update(1 - Math.Pow(1 - fraction, 3));
        }
        if (transitions.Count == 0) timer.Stop();
    }

    public void Dispose()
    {
        timer.Dispose();
        foreach (var transition in transitions.Values.ToArray()) transition.Update(1);
        transitions.Clear();
    }

    public static Color Blend(Color from, Color to, double fraction) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * fraction),
        (int)(from.G + (to.G - from.G) * fraction),
        (int)(from.B + (to.B - from.B) * fraction));
}
