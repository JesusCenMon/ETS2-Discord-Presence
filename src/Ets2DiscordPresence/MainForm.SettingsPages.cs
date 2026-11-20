namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private sealed class SettingsPages : TableLayoutPanel
    {
        private readonly Panel rail = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        private readonly Panel host = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Dictionary<Panel, NavigationButton> links = [];
        private Panel? selected;
        private Action<Control>? transition;
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
            host.Controls.Add(page);
            if (selected is null) SelectedTab = page;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal Panel? SelectedTab
        {
            get => selected;
            set
            {
                if (value is null || value == selected) return;
                transition?.Invoke(host);
                host.SuspendLayout();
                var previous = selected;
                selected = value;
                value.BringToFront();
                value.Show();
                previous?.Hide();
                host.ResumeLayout(true);
                foreach (var (page, link) in links) link.SetSelected(page == value);
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void BuildNavigation(ShellPalette palette, Func<UiMotion> motion, Action<Control> transition)
        {
            this.transition = transition;
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
                    Text = page.Text, AccessibleName = page.Text, Palette = palette, Dock = DockStyle.Top, SelectionMotion = motion
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
