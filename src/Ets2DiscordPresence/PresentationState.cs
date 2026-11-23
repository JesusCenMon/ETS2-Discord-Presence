using System.Text.Json;

namespace Ets2DiscordPresence;

internal sealed class BufferedSurface : Panel
{
    internal BufferedSurface()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
    }
}

internal sealed class SettingsDraft(IEnumerable<Control> pages)
{
    private readonly Dictionary<Control, string> original = pages.SelectMany(Inputs).Distinct()
        .ToDictionary(control => control, Value);
    internal bool Dirty => original.Any(item => !item.Key.IsDisposed && Value(item.Key) != item.Value);
    internal int Changed => original.Count(item => !item.Key.IsDisposed && Value(item.Key) != item.Value);
    internal void Restore(Control page)
    {
        foreach (var control in Inputs(page))
            if (original.TryGetValue(control, out var value)) RestoreValue(control, value);
    }
    private static IEnumerable<Control> Inputs(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is CheckBox or ComboBox or TextBox or TrackBar or CheckedListBox) yield return child;
            foreach (var nested in Inputs(child)) yield return nested;
        }
    }
    private static string Value(Control control) => control switch
    {
        CheckBox check => check.Checked.ToString(),
        ComboBox combo => combo.SelectedIndex.ToString(),
        TextBox text => text.Text,
        TrackBar slider => slider.Value.ToString(),
        CheckedListBox list => JsonSerializer.Serialize(list.Items.Cast<object>().Select((item, index) =>
            new OrderState(item.ToString() ?? "", list.GetItemChecked(index))).ToArray()),
        _ => ""
    };
    private sealed record OrderState(string Text, bool Checked);
    private static void RestoreValue(Control control, string value)
    {
        switch (control)
        {
            case CheckBox check: check.Checked = bool.Parse(value); break;
            case ComboBox combo: combo.SelectedIndex = Math.Clamp(int.Parse(value), -1, combo.Items.Count - 1); break;
            case TextBox text: text.Text = value; break;
            case TrackBar slider: slider.Value = Math.Clamp(int.Parse(value), slider.Minimum, slider.Maximum); break;
            case CheckedListBox list:
                var saved = JsonSerializer.Deserialize<OrderState[]>(value)!;
                var items = list.Items.Cast<object>().ToList();
                list.Items.Clear();
                foreach (var item in saved)
                {
                    var found = items.FirstOrDefault(candidate => candidate.ToString() == item.Text);
                    if (found is not null) { list.Items.Add(found, item.Checked); items.Remove(found); }
                }
                break;
        }
    }
}

internal static class PresentationState
{
    internal static string ReplayStatus(bool playing, long elapsed, long duration, bool english) =>
        playing ? english ? "Playing" : "Reproduciendo"
        : elapsed >= duration && duration > 0 ? english ? "Finished" : "Terminada"
        : english ? "Paused" : "En pausa";
    internal static string? Freshness(bool running, bool received, long age, bool english) =>
        !running ? null : !received ? english ? "Waiting for game data" : "Esperando datos del juego"
        : age < 4500 ? null
        : english ? $"Data last received {Math.Max(0, age / 1000)} s ago" : $"Últimos datos recibidos hace {Math.Max(0, age / 1000)} s";
}
