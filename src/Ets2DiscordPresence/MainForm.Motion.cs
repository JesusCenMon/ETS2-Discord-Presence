namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private UiMotion? motion;
    private UiMotion Motion => motion ??= new UiMotion(() => CanAnimate);
    private bool CanAnimate => Visible && WindowState != FormWindowState.Minimized && !preferences.Features.ReduceMotion
        && !SystemInformation.HighContrast && SystemInformation.IsMenuAnimationEnabled && UiMotion.SystemAnimationsEnabled;

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
