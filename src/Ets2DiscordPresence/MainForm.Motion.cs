namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private UiMotion? motion;
    private UiMotion Motion => motion ??= new UiMotion(() => CanAnimate);
    private bool CanAnimate => Visible && WindowState != FormWindowState.Minimized && !preferences.Features.ReduceMotion
        && !SystemInformation.HighContrast && SystemInformation.IsMenuAnimationEnabled;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Label, TextInk> animatedInk = new();
    private sealed record TextInk(Color Resting);

    private object? artworkTransition;

    private void AnimateArtwork(Image? incoming)
    {
        if (!CanAnimate || !homeLarge.Visible || homeLarge.Image is not { } outgoing || ReferenceEquals(outgoing, incoming)) return;
        if (artworkTransition is { } previous) Motion.Complete(previous);
        var snapshot = new Bitmap(outgoing);
        var opacity = 1f;
        var key = artworkTransition = new object();
        void Paint(object? sender, PaintEventArgs args)
        {
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity });
            args.Graphics.DrawImage(snapshot, homeLarge.ClientRectangle, 0, 0,
                snapshot.Width, snapshot.Height, GraphicsUnit.Pixel, attributes);
        }
        homeLarge.Paint += Paint;
        Motion.Run(key, homeLarge, 260, value =>
        {
            if (value >= 1)
            {
                artworkTransition = null;
                homeLarge.Paint -= Paint;
                snapshot.Dispose();
            }
            opacity = (float)(1 - value);
            if (!homeLarge.IsDisposed) homeLarge.Invalidate();
        });
    }

    private void AnimateButton(Button button)
    {
        if (!CanAnimate || !button.Visible || button.IsDisposed) return;
        var resting = button.BackColor;
        var ink = Equals(button.Tag, "primary") ? shellPalette.OnAccent : shellPalette.Accent;
        Motion.Run(button, button, 190, value =>
        {
            if (!button.IsDisposed) button.BackColor = UiMotion.Blend(UiMotion.Blend(resting, ink, .18), resting, value);
        });
    }

    private void AnimateText(Label label, Color accent, int duration = 320)
    {
        if (!CanAnimate || !label.Visible || label.IsDisposed) return;
        var rest = animatedInk.GetValue(label, target => new(target.ForeColor)).Resting;
        Motion.Run(label, label, duration, amount =>
        {
            if (label.IsDisposed) return;
            label.ForeColor = UiMotion.Blend(accent, rest, amount);
            if (amount >= 1) animatedInk.Remove(label);
        });
    }

    private void ObserveText(Label label, Func<Color> accent)
    {
        label.TextChanged += (_, _) =>
        {
            if (label.Visible && label.Text.Length > 0) AnimateText(label, accent());
        };
    }

    private void Reveal(Control control)
    {
        AnimateSurface(control, shellPalette.Hover, 220);
    }

    private void AnimateSurface(Control control, Color accent, int duration)
    {
        if (!CanAnimate || !control.Visible || control.IsDisposed) return;
        var resting = control.BackColor;
        Motion.Run(control, control, duration, value =>
        {
            if (!control.IsDisposed) control.BackColor = UiMotion.Blend(accent, resting, value);
        });
    }

    private void AnimatePageChange(Control host, int duration = UiMotion.Normal)
    {
        Motion.Complete(host);
        if (!CanAnimate || !host.Visible || host.ClientSize.Width < 1 || host.ClientSize.Height < 1) return;
        if ((long)host.Width * host.Height > 4_000_000) return;

        var snapshot = new Bitmap(host.Width, host.Height);
        try { host.DrawToBitmap(snapshot, new Rectangle(Point.Empty, snapshot.Size)); }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException) { snapshot.Dispose(); return; }
        var overlay = new PageSnapshot(snapshot) { Bounds = host.ClientRectangle, TabStop = false };
        host.Controls.Add(overlay);
        overlay.BringToFront();
        Motion.Run(host, host, duration, value =>
        {
            if (overlay.IsDisposed) return;
            if (value >= 1) overlay.Dispose();
            else
            {
                if (host.Controls.GetChildIndex(overlay) != 0) overlay.BringToFront();
                var width = Math.Max(1, (int)(snapshot.Width * (1 - value)));
                overlay.SetBounds(snapshot.Width - width, 0, width, snapshot.Height);
                overlay.Invalidate();
            }
        });
    }

    private sealed class PageSnapshot : Control
    {
        private readonly Bitmap snapshot;

        internal PageSnapshot(Bitmap snapshot)
        {
            this.snapshot = snapshot;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x20;
                return parameters;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = -1; return; }
            base.WndProc(ref message);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImage(snapshot, ClientRectangle, Left, 0, Width, Height, GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) snapshot.Dispose();
            base.Dispose(disposing);
        }
    }

    private void AnimatePageEdge(Control page)
    {
        if (!CanAnimate || !page.Visible) return;
        var edge = new Panel { Height = 2, Width = page.ClientSize.Width, TabStop = false, BackColor = shellPalette.Accent };
        edge.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        page.Controls.Add(edge);
        edge.BringToFront();
        Motion.Run(edge, page, 240, amount =>
        {
            if (edge.IsDisposed) return;
            if (amount >= 1) edge.Dispose();
            else edge.BackColor = UiMotion.Blend(shellPalette.Accent, shellPalette.Canvas, amount);
        });
    }
}
