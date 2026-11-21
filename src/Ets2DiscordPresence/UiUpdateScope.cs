using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ets2DiscordPresence;

internal sealed class UiRedrawScope : IDisposable
{
    private sealed class State { internal int Depth; internal nint Handle; }
    private static readonly ConditionalWeakTable<Control, State> states = new();
    private readonly Control control;
    private readonly State state;
    private bool disposed;

    internal UiRedrawScope(Control control)
    {
        this.control = control;
        state = states.GetOrCreateValue(control);
        if (state.Depth++ == 0 && control.IsHandleCreated && control.Visible)
        {
            state.Handle = control.Handle;
            SendMessage(state.Handle, 0x000B, 0, 0);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (--state.Depth != 0 || state.Handle == 0) return;
        var handle = state.Handle;
        state.Handle = 0;
        if (control.IsDisposed || !control.IsHandleCreated || control.Handle != handle) return;
        SendMessage(handle, 0x000B, 1, 0);
        control.Invalidate(true);
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}

internal sealed class UiLayoutBatch : IDisposable
{
    [ThreadStatic] private static UiLayoutBatch? current;
    private readonly UiLayoutBatch? previous;
    private readonly List<Control> held = [];
    private bool disposed;

    internal UiLayoutBatch(Control? root = null)
    {
        previous = current;
        current = this;
        if (root is not null) HoldTree(root);
    }

    internal static T Created<T>(T control) where T : Control
    {
        current?.Hold(control);
        return control;
    }

    private void Hold(Control control)
    {
        control.SuspendLayout();
        held.Add(control);
    }

    private void HoldTree(Control control)
    {
        Hold(control);
        foreach (Control child in control.Controls) HoldTree(child);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        current = previous;
        for (var index = held.Count - 1; index >= 0; index--)
            if (!held[index].IsDisposed) held[index].ResumeLayout(false);
    }
}

