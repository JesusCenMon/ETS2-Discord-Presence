namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private sealed class SettingsPages : TableLayoutPanel
    {
        private readonly Panel rail = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        private readonly Panel host = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Dictionary<Panel, NavigationButton> links = [];
        private Panel? selected;
        private readonly HashSet<Panel> prepared = [];
        internal Action<Control>? PreparePage;
        internal Func<string, IDisposable>? Measure;
        public List<Panel> Pages { get; } = [];
        public int ContentWidth => host.ClientSize.Width;
        public event EventHandler? SelectedIndexChanged;

        public SettingsPages()
        {
            DoubleBuffered = true;
            ColumnCount = 2;
            RowCount = 1;
            Margin = Padding.Empty;
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 192));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(rail, 0, 0);
            Controls.Add(host, 1, 0);
            host.ClientSizeChanged += (_, _) => OnClientSizeChanged(EventArgs.Empty);
        }

        public void Add(Panel page)
        {
            Pages.Add(page);
            page.Visible = false;
            // Unvisited sections stay detached, so Windows does not create their native controls.
            page.SuspendLayout();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Panel? SelectedTab
        {
            get => selected;
            set
            {
                if (value is null || value == selected) return;
                using var measurement = Measure?.Invoke("settings-" + Pages.IndexOf(value));
                using var redraw = new UiRedrawScope(host);
                using var layout = new UiLayoutBatch(value);
                host.SuspendLayout();
                host.BackColor = value.BackColor;
                var previous = selected;
                selected = value;
                if (prepared.Add(value))
                {
                    value.Size = host.ClientSize;
                    PreparePage?.Invoke(value);
                    foreach (Control content in value.Controls) content.ResumeLayout(true);
                    value.ResumeLayout(true);
                }
                if (value.Parent != host) host.Controls.Add(value);
                value.BringToFront();
                value.Show();
                previous?.Hide();
                layout.Dispose();
                value.PerformLayout();
                host.ResumeLayout(true);
                foreach (var (page, link) in links) link.SetSelected(page == value);
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                foreach (var page in Pages.Where(page => page.Parent is null)) page.Dispose();
            base.Dispose(disposing);
        }

        public void BuildNavigation(ShellPalette palette, Func<UiMotion> motion)
        {
            if (selected is null && Pages.Count > 0) SelectedTab = Pages[0];
            string[] icons = ["desktop", "eye", "vehicle_truck", "weather_sunny", "book_open", "options", "play_circle"];
            rail.BackColor = palette.Surface;
            var menu = Grid(1);
            menu.Padding = new Padding(8);
            var tips = new ToolTip();
            Disposed += (_, _) => tips.Dispose();
            for (var index = 0; index < Pages.Count; index++)
            {
                var page = Pages[index];
                var link = new NavigationButton(icons[index % icons.Length])
                {
                    Text = page.Text,
                    AccessibleName = page.Text,
                    Palette = palette,
                    Width = 1,
                    Dock = DockStyle.Top,
                    SelectionMotion = motion
                };
                tips.SetToolTip(link, page.Text);
                link.Click += (_, _) => SelectedTab = page;
                links.Add(page, link);
                link.SetSelected(page == selected);
                AddRows(menu, link);
            }
            rail.Controls.Add(menu);
            void ResizeRail()
            {
                var scale = DeviceDpi / 96f;
                var iconsOnly = Width / scale < 660;
                var width = (iconsOnly ? 56 : 192 + Math.Max(0, (links.Values.FirstOrDefault()?.Font.SizeInPoints ?? 10.5f) - 10.5f) * 7) * scale;
                if (Math.Abs(ColumnStyles[0].Width - width) > .5f) ColumnStyles[0].Width = width;
                menu.MaximumSize = new Size(Math.Max(24, (int)width - (rail.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0)), 0);
                foreach (var link in links.Values)
                {
                    link.IconsOnly = iconsOnly;
                    link.Invalidate();
                }
            }
            SizeChanged += (_, _) => ResizeRail();
            foreach (var link in links.Values) link.FontChanged += (_, _) => ResizeRail();
            ResizeRail();
        }
    }
}
