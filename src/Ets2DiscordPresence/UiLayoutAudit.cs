namespace Ets2DiscordPresence;

internal static class UiLayoutAudit
{
    internal sealed record Finding(string Kind, string ControlType);
    internal static IReadOnlyList<Finding> Inspect(Control root)
    {
        var findings = new List<Finding>();
        foreach (Control child in root.Controls)
        {
            if (!child.Visible) continue;
            // Scrollable content and explicit ellipsis are intentional. Detect lost line height,
            // and action text whose actual allocated rectangle cannot contain it.
            if (child is Label { AutoSize: false, Text.Length: > 0 } label
                && label.Height - label.Padding.Vertical < label.Font.Height)
                findings.Add(new("label-height", nameof(Label)));
            if (child is Button { Text.Length: > 0, AutoEllipsis: false } button && button.GetType().Name != "NavigationButton")
            {
                var text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine);
                var icon = button.Image is null ? 0 : button.Image.Width + 6;
                if (button.Width < text.Width + button.Padding.Horizontal + icon)
                    findings.Add(new("button-width", nameof(Button)));
            }
            findings.AddRange(Inspect(child));
        }
        return findings;
    }
}
