namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private readonly Label sessionNotice = TextLabel("", 10, true);
    private readonly Label homeFreshness = TextLabel("", 9);
    private readonly Label tripFreshness = TextLabel("", 9);
    private SettingsDraft? settingsDraft;
    private Action<Action>? settingsLeavePrompt;
    private bool bypassSettingsGuard;
    private Action? afterSettingsSave;
    private InteractionFilter? interactionFilter;
    private bool UiBusy => interactionFilter?.Busy == true;

    private sealed class InteractionFilter(Control root) : IMessageFilter
    {
        private long typingAt;
        internal bool Busy => Environment.TickCount64 - typingAt < 650;
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg is not (0x100 or 0x102 or 0x115 or 0x114 or 0x20A)) return false;
            var control = Control.FromHandle(message.HWnd);
            var ancestor = control;
            while (ancestor is not null && ancestor != root) ancestor = ancestor.Parent;
            if (ancestor == root) typingAt = Environment.TickCount64;
            return false;
        }
    }

    private bool GuardSettingsNavigation(Action next)
    {
        if (bypassSettingsGuard || currentPage != "settings" || settingsDraft?.Dirty != true || settingsLeavePrompt is null) return false;
        settingsLeavePrompt(next);
        foreach (var (key, button) in navigation) button.SetSelected(key == currentPage);
        return true;
    }

    private void RenderPresentationStatus()
    {
        var shortNotice = ClientSize.Width / (DeviceDpi / 96f) < 800 || ClientSize.Height / (DeviceDpi / 96f) < 600;
        var message = replayOpen ? L("Repetición local · Tu actividad no se publica en Discord", "Local replay · Your activity is not published to Discord")
            : demo || demoScenario is not null ? L("Demostración · Estos datos son de ejemplo y no se publican", "Demo · These are sample data and are not published") : "";
        if (shortNotice && message.Length > 0)
            message = replayOpen ? L("Repetición local · Sin publicar", "Local replay · Not publishing")
                : L("Ejemplo local · Sin publicar", "Demo · Not publishing");
        SetWrap(sessionNotice, Math.Max(100, pageHost.ClientSize.Width - 32));
        sessionNotice.Text = message;
        sessionNotice.Visible = message.Length > 0;
        sessionNotice.BackColor = UiMotion.Blend(shellPalette.Canvas, shellPalette.Warning, .12);
        sessionNotice.ForeColor = shellPalette.Warning;
        var stale = PresentationState.Freshness(gameRunning, lastGoodTelemetryAt > 0,
            Environment.TickCount64 - lastGoodTelemetryAt, English);
        foreach (var label in new[] { homeFreshness, tripFreshness })
        {
            label.Text = demo || demoScenario is not null || lastGoodTelemetryAt == 0 ? "" : stale ?? "";
            label.Visible = label.Text.Length > 0;
            label.ForeColor = shellPalette.Warning;
        }
    }

    private IDisposable PreserveScroll(ScrollableControl control)
    {
        var before = control.AutoScrollPosition;
        return new OnDispose(() =>
        {
            if (control.IsDisposed || !control.Visible || !control.AutoScroll) return;
            if (control.AutoScrollPosition != before) control.AutoScrollPosition = new Point(-before.X, -before.Y);
        });
    }

    private void FullTextHint(Control control)
    {
        void Update() => shellTips.SetToolTip(control, control.Text);
        control.TextChanged += (_, _) => Update();
        Update();
    }

    private void AdvancedSettings(TableLayoutPanel content, string[] actionNames)
    {
        var advanced = Grid(1);
        advanced.Visible = false;
        var toggle = MakeButton(L("Opciones avanzadas", "Advanced options"));
        toggle.Tag = "quiet";
        foreach (var child in content.Controls.Cast<Control>().Where(child => actionNames.Contains(child.Text)).ToArray())
        {
            content.Controls.Remove(child);
            AddRows(advanced, child);
        }
        if (advanced.Controls.Count == 0) { advanced.Dispose(); toggle.Dispose(); return; }
        void Expand()
        {
            advanced.Visible = true;
            toggle.Text = L("Ocultar opciones avanzadas", "Hide advanced options");
        }
        advanced.Tag = (Action)Expand;
        toggle.Click += (_, _) =>
        {
            if (advanced.Visible) { advanced.Hide(); toggle.Text = L("Opciones avanzadas", "Advanced options"); }
            else Expand();
        };
        AddRows(content, toggle, advanced);
    }

    private void ConfigureSettingsEditor(LibraryPage root, Form window, SettingsPages tabs,
        FlowLayoutPanel footer, Button apply, Button cancel, TrackBar textSize, ComboBox theme,
        ComboBox accent, Label error)
    {
        var draft = settingsDraft = new SettingsDraft(tabs.Pages);
        Action? pending = null;
        var baselineTheme = theme.SelectedIndex;
        var status = TextLabel("", 9, true);
        status.AccessibleName = L("Estado de los ajustes", "Settings state");
        var keep = MakeButton(L("Seguir editando", "Keep editing"));
        var discard = MakeButton(L("Descartar y continuar", "Discard and continue"));
        keep.Visible = discard.Visible = false;
        var reset = MakeButton(L("Deshacer cambios del apartado", "Undo section edits"));
        reset.Tag = "quiet";
        reset.Click += (_, _) => { if (tabs.SelectedTab is { } page) draft.Restore(page); Render(); };
        var statusRow = ToolBar();
        statusRow.Controls.AddRange([status, keep, discard, reset]);
        var footerFrame = Grid(1);
        footerFrame.Dock = DockStyle.Bottom;
        footerFrame.BackColor = shellPalette.Surface;
        statusRow.Padding = new Padding(16, 0, 16, 0);
        footer.Dock = DockStyle.Top;
        error.ForeColor = shellPalette.Error;
        error.Padding = new Padding(16, 4, 16, 4);
        error.Visible = false;
        AddRows(footerFrame, error, statusRow, footer);
        window.Controls.Remove(footer);
        window.Controls.Add(footerFrame);

        void Render()
        {
            var changed = draft.Changed;
            status.Text = pending is not null ? L("Hay cambios sin guardar. Guarda o descarta para continuar.", "Unsaved changes. Save or discard to continue.")
                : changed == 0 ? L("Sin cambios pendientes", "No pending changes")
                : L($"{changed} cambios sin guardar", $"{changed} unsaved changes");
            if (theme.SelectedIndex != baselineTheme)
                status.Text += L(" · El tema nativo requiere reiniciar", " · Native theme requires restart");
            status.ForeColor = changed > 0 ? shellPalette.Warning : shellPalette.Muted;
            status.Visible = changed > 0 || pending is not null;
            keep.Visible = discard.Visible = pending is not null;
            reset.Enabled = changed > 0;
            reset.Visible = changed > 0;
            apply.Enabled = true;
        }
        settingsLeavePrompt = next => { pending = next; afterSettingsSave = next; Render(); };
        keep.Click += (_, _) => { pending = null; afterSettingsSave = null; Render(); };
        discard.Click += (_, _) =>
        {
            var next = pending;
            pending = null;
            root.Context = "";
            bypassSettingsGuard = true;
            try { next?.Invoke(); }
            finally { bypassSettingsGuard = false; }
        };
        void FitFooter()
        {
            var width = Math.Max(120, window.ClientSize.Width);
            var compact = width < 760 * DeviceDpi / 96;
            footerFrame.MaximumSize = new Size(width, 0);
            footer.MaximumSize = new Size(width, 0);
            statusRow.MaximumSize = new Size(width, 0);
            footer.Width = statusRow.Width = width;
            SetWrap(status, width - 48);
            SetWrap(error, width - 48);
            reset.Text = compact ? L("Deshacer", "Undo edits") : L("Deshacer cambios del apartado", "Undo section edits");
            discard.Text = compact ? L("Descartar", "Discard") : L("Descartar y continuar", "Discard and continue");
            foreach (var button in new[] { apply, cancel, keep, discard, reset })
            {
                button.MaximumSize = new Size(width - 40, 0);
                button.AutoEllipsis = true;
            }
        }
        window.ClientSizeChanged += (_, _) => FitFooter();
        footer.FontChanged += (_, _) => FitFooter();
        FitFooter();
        var poll = new System.Windows.Forms.Timer { Interval = 250 };
        poll.Tick += (_, _) => { if (root.Visible) { afterSettingsSave = pending; Render(); } };
        root.VisibleChanged += (_, _) => { if (root.Visible) { poll.Start(); Render(); } else poll.Stop(); };
        root.Disposed += (_, _) =>
        {
            poll.Dispose();
            if (ReferenceEquals(settingsDraft, draft)) { settingsDraft = null; settingsLeavePrompt = null; afterSettingsSave = null; }
        };
        cancel.Click += (_, _) => { pending = null; afterSettingsSave = null; };
        Render();
    }
}
