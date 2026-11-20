namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private sealed class SettingSwitch(Func<ShellPalette> theme, Func<UiMotion> motion, string yes, string no) : CheckBox
    {
        private double position;

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            var target = Checked ? 1d : 0d;
            var start = position;
            motion().Run(this, this, UiMotion.Normal, progress =>
            {
                if (IsDisposed) return;
                position = start + (target - start) * progress;
                Invalidate();
            });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = theme();
            var graphics = e.Graphics;
            var surface = BackColor == Parent?.BackColor ? palette.Surface : BackColor;
            graphics.Clear(surface);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var scale = DeviceDpi / 96f;
            var width = (int)(54 * scale);
            var height = (int)(24 * scale);
            var track = new Rectangle(Width - width - 12, (Height - height) / 2, width, height);
            using var outline = Rounded(track, height / 2);
            using var fill = new SolidBrush(UiMotion.Blend(palette.Hover, palette.Accent, position));
            graphics.FillPath(fill, outline);
            using var border = new Pen(palette.Border);
            graphics.DrawPath(border, outline);
            var thumb = height - 8;
            using var knob = new SolidBrush(UiMotion.Blend(palette.Text, palette.OnAccent, position));
            graphics.FillEllipse(knob, track.X + 4 + (float)(width - height) * (float)position, track.Y + 4, thumb, thumb);
            var text = new Rectangle(12, 3, Math.Max(1, track.X - 22), Height - 6);
            TextRenderer.DrawText(graphics, Text, Font, text, Enabled ? palette.Text : palette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            TextRenderer.DrawText(graphics, Checked ? yes : no, SystemFonts.CaptionFont, track,
                Checked ? palette.OnAccent : palette.Text,
                TextFormatFlags.VerticalCenter | (Checked ? TextFormatFlags.Left : TextFormatFlags.Right));
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -2, -2), palette.Text, palette.Surface);
        }
    }
}
