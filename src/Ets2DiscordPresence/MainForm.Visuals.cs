namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private static Bitmap LoadFluent(string name)
    {
        using var input = AppResources.Open($"fluent.{name}.png");
        using var image = Image.FromStream(input);
        return new Bitmap(image);
    }

    private static void SetIcon(Button button, string name, Color ink, bool iconOnly = false)
    {
        using var source = LoadFluent(name);
        var size = Math.Max(16, 16 * button.DeviceDpi / 96);
        var icon = new Bitmap(size, size);
        using var g = Graphics.FromImage(icon);
        DrawIcon(g, source, new Rectangle(0, 0, size, size), ink);
        button.Image = icon;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.ImageAlign = iconOnly ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
        if (iconOnly) button.Text = "";
        button.Disposed += (_, _) => icon.Dispose();
    }

    private static void DrawIcon(Graphics g, Image icon, Rectangle bounds, Color ink)
    {
        using var tint = new System.Drawing.Imaging.ImageAttributes();
        tint.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix
        {
            Matrix00 = 0, Matrix11 = 0, Matrix22 = 0,
            Matrix40 = ink.R / 255f, Matrix41 = ink.G / 255f, Matrix42 = ink.B / 255f
        });
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(icon, bounds, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, tint);
    }

    private Control EmptyState(string iconName, string name)
    {
        var panel = new Panel { BackColor = shellPalette.Surface, TabStop = false, AccessibleName = name };
        var icon = LoadFluent(iconName);
        panel.Paint += (_, e) =>
        {
            var size = Math.Min(42, Math.Min(panel.Width, panel.Height));
            if (size > 0)
                DrawIcon(e.Graphics, icon, new Rectangle((panel.Width - size) / 2, (panel.Height - size) / 2, size, size), shellPalette.Muted);
        };
        panel.Disposed += (_, _) => icon.Dispose();
        return panel;
    }

    private FlowLayoutPanel DialogFooter() => new()
    {
        Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
        Padding = new Padding(16, 8, 16, 8), BackColor = shellPalette.Surface
    };

    private void StyleDialogSurface(Control control)
    {
        switch (control)
        {
            case Form:
                control.BackColor = shellPalette.Canvas;
                control.ForeColor = shellPalette.Text;
                break;
            case Button button when button is not NavigationButton:
                StyleButton(button);
                break;
            case ListBox or ComboBox or TextBox:
                control.BackColor = shellPalette.Surface;
                control.ForeColor = shellPalette.Text;
                break;
            case Label label when label.ForeColor == Foreground:
                label.ForeColor = Equals(label.Tag, "muted") ? shellPalette.Muted : shellPalette.Text;
                break;
            case TripMap map:
                map.Canvas = shellPalette.Surface;
                map.Ink = shellPalette.Text;
                map.TrackInk = shellPalette.Accent;
                break;
            case ShellRule rule:
                rule.Ink = shellPalette.Border;
                break;
        }
        foreach (Control child in control.Controls) StyleDialogSurface(child);
    }

    private void StyleButton(Button button)
    {
        button.Click -= AnimateButtonClick;
        button.Click += AnimateButtonClick;
        if (button is FocusButton styled)
        {
            styled.Palette = shellPalette;
            styled.Invalidate();
        }
        var primary = Equals(button.Tag, "primary");
        var quiet = Equals(button.Tag, "quiet");
        var danger = Equals(button.Tag, "danger");
        button.BackColor = primary ? shellPalette.Accent : quiet ? shellPalette.Canvas
            : danger ? UiMotion.Blend(shellPalette.Surface, shellPalette.Error, .14) : shellPalette.Surface;
        button.ForeColor = primary ? shellPalette.OnAccent : shellPalette.Text;
        button.FlatAppearance.BorderSize = quiet ? 0 : 1;
        button.FlatAppearance.BorderColor = primary ? shellPalette.Accent : shellPalette.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? shellPalette.Accent : shellPalette.Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? shellPalette.Accent : shellPalette.Hover;
    }

    private void AnimateButtonClick(object? sender, EventArgs e)
    {
        if (sender is Button { IsDisposed: false } button) AnimateButton(button);
    }

    private sealed class ThemeSample(ShellPalette palette, string title, string selected, string action) : Control
    {
        protected override Size DefaultSize => new(164, 88);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(palette.Canvas);
            using var border = new Pen(palette.Border);
            using var selectedFill = new SolidBrush(palette.Hover);
            using var actionFill = new SolidBrush(palette.Accent);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, title, Font, new Rectangle(10, 8, Width - 20, 24), palette.Text);
            g.FillRectangle(selectedFill, 10, 35, Width - 20, 20);
            TextRenderer.DrawText(g, selected, Font, new Rectangle(14, 35, Width - 28, 20), palette.Text, TextFormatFlags.VerticalCenter);
            g.FillRectangle(actionFill, 10, 60, 70, 20);
            TextRenderer.DrawText(g, action, Font, new Rectangle(10, 60, 70, 20), palette.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private sealed class FocusButton : Button
    {
        internal ShellPalette? Palette;
        private bool hovered;
        private bool pressed;

        public override Size GetPreferredSize(Size proposedSize)
        {
            var preferred = base.GetPreferredSize(proposedSize);
            if (!AutoSize || Text.Length == 0) return preferred;
            var flags = TextFormatFlags.SingleLine | (UseMnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix);
            var text = TextRenderer.MeasureText(Text, Font, Size.Empty, flags);
            var iconWidth = Image is { } icon ? icon.Width + 6 : 0;
            return new Size(Math.Max(preferred.Width, text.Width + Padding.Horizontal + iconWidth + 4), preferred.Height);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Palette is not { } palette || SystemInformation.HighContrast || Width < 20 || Height < 20)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            var parentColor = Parent?.BackColor ?? palette.Canvas;
            g.Clear(parentColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var primary = Equals(Tag, "primary");
            var quiet = Equals(Tag, "quiet");
            var ink = Enabled ? ForeColor : palette.Muted;
            var resting = quiet ? parentColor : BackColor;
            var color = Enabled && (hovered || pressed)
                ? UiMotion.Blend(resting, ink, pressed ? .18 : .09)
                : Enabled ? resting : UiMotion.Blend(resting, parentColor, .55);
            using var outline = Rounded(new Rectangle(1, 1, Width - 3, Height - 3), Math.Max(6, 8 * DeviceDpi / 96));
            using var fill = new SolidBrush(color);
            g.FillPath(fill, outline);
            if (Focused && ShowFocusCues || !quiet && !primary)
            {
                using var border = new Pen(Focused && ShowFocusCues ? primary ? palette.OnAccent : palette.Accent : palette.Border,
                    Focused && ShowFocusCues ? 2f : 1f);
                g.DrawPath(border, outline);
            }

            var label = Rectangle.Inflate(ClientRectangle, -Padding.Left, 0);
            if (Image is { } icon)
            {
                var x = Text.Length == 0 ? (Width - icon.Width) / 2 : label.Left;
                g.DrawImageUnscaled(icon, x, (Height - icon.Height) / 2);
                if (Text.Length > 0)
                {
                    var used = icon.Width + 6;
                    label.X += used;
                    label.Width = Math.Max(0, label.Width - used);
                }
            }
            var alignment = TextAlign == ContentAlignment.MiddleLeft || Image is not null
                ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            var flags = alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            if (label.Width > 0) TextRenderer.DrawText(g, Text, Font, label, ink, flags);
        }
    }

    private sealed class RouteProgress : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal ShellPalette Palette { get; set; } = ShellPalette.Dark;
        private int value;
        private double displayed;
        internal void SetValue(int target, UiMotion motion)
        {
            if (value == target) return;
            value = target;
            var start = displayed;
            motion.Run(this, this, 420, amount =>
            {
                if (IsDisposed) return;
                displayed = start + (target - start) * amount;
                Invalidate();
            });
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Paused
        {
            get => paused;
            set
            {
                if (paused == value) return;
                paused = value;
                Invalidate();
            }
        }
        private bool paused;

        public RouteProgress()
        {
            Height = 8;
            Dock = DockStyle.Top;
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var track = new SolidBrush(Palette.Hover);
            using var fill = new SolidBrush(Paused ? Palette.Muted : Palette.Accent);
            e.Graphics.FillRectangle(track, ClientRectangle);
            e.Graphics.FillRectangle(fill, 0, 0, (float)(Width * displayed / 100), Height);
        }
    }
}
