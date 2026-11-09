using System.Drawing.Drawing2D;

namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private readonly TableLayoutPanel appShell = new()
    {
        Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
    };
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Panel sidebar = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(8) };
    private readonly Panel homePage = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Dictionary<string, NavigationButton> navigation = [];
    private readonly Dictionary<string, NavigationButton> themeButtons = [];
    private readonly ToolTip shellTips = new();
    private readonly Label sidebarGame = TextLabel("", 9);
    private readonly Label sidebarDiscord = TextLabel("", 9);
    private readonly Label homeHeadline = TextLabel("", 12, true);
    private readonly Label homeDetail = TextLabel("", 11, true);
    private readonly Label homeState = TextLabel("", 10);
    private readonly Label homeStatus = TextLabel("", 9);
    private readonly Label homeGame = TextLabel("", 9);
    private readonly PictureBox homeLarge = new() { Size = new Size(72, 54), SizeMode = PictureBoxSizeMode.Zoom };
    private readonly PictureBox homeBadge = new() { Size = new Size(24, 24), SizeMode = PictureBoxSizeMode.Zoom };
    private readonly FlowLayoutPanel homeImages = ToolBar();
    private Panel tripShellPage = null!;
    private FlowLayoutPanel themeStrip = null!;
    private string currentPage = "home";
    private bool narrowShell;
    private bool hasShellStatus;
    private bool shellGameOpen;
    private bool shellDiscordReady;
    private ShellPalette shellPalette = ShellPalette.Dark;

    private void BuildShell(Panel tripPage)
    {
        tripShellPage = tripPage;
        appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        appShell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        appShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        appShell.Controls.Add(sidebar, 0, 0);
        appShell.Controls.Add(pageHost, 1, 0);
        pageHost.Controls.Add(tripShellPage);
        pageHost.Controls.Add(homePage);
        Controls.Add(appShell);
        Disposed += (_, _) => shellTips.Dispose();

        var menu = Grid(1);
        menu.Padding = new Padding(0, 2, 0, 0);
        AddNavigation(menu, "home", "home", "Inicio", "Home", () => Navigate("home"));
        AddNavigation(menu, "trip", "vehicle_truck", "Viaje", "Trip", () => Navigate("trip"));
        AddNavigation(menu, "journal", "book_open", "Diario", "Journal", ShowHistory);
        AddNavigation(menu, "replay", "play_circle", "Repeticiones", "Replays", ShowRecordings);
        AddRows(menu, new ShellRule { Margin = new Padding(0, 12, 0, 12) });
        AddNavigation(menu, "display", "eye", "Apariencia", "Display", () => ShowSettings("display"));
        AddNavigation(menu, "behavior", "options", "Comportamiento", "Behavior", () => ShowSettings("behavior"));
        AddNavigation(menu, "advanced", "settings", "Avanzado", "Advanced", () => ShowSettings("advanced"));
        AddRows(menu, new ShellRule { Margin = new Padding(0, 12, 0, 12) });
        AddNavigation(menu, "help", "question_circle", "Ayuda", "Help", ShowConnectionGuide);
        AddNavigation(menu, "about", "info", "Acerca de", "About", ShowAbout);

        var footer = Grid(1);
        footer.Dock = DockStyle.Bottom;
        sidebarGame.Padding = sidebarDiscord.Padding = new Padding(8, 0, 0, 0);
        AddRows(footer, sidebarGame, sidebarDiscord, new ShellRule { Margin = new Padding(0, 8, 0, 8) });
        AddNavigation(footer, "github", "code", "GitHub", "GitHub", () => OpenExternal("https://github.com/JesusCenMon/ETS2-Discord-Presence"));
        themeStrip = ToolBar();
        themeStrip.Margin = new Padding(0, 6, 0, 0);
        AddTheme("system", "desktop", "Usar tema del sistema", "Follow system theme");
        AddTheme("day", "weather_sunny", "Tema claro", "Light theme");
        AddTheme("night", "weather_moon", "Tema oscuro", "Dark theme");
        AddRows(footer, themeStrip);
        var menuViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        menuViewport.Controls.Add(menu);
        sidebar.Controls.Add(menuViewport);
        sidebar.Controls.Add(footer);
        sidebar.Paint += (_, e) =>
        {
            using var border = new Pen(shellPalette.Border);
            e.Graphics.DrawLine(border, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        };

        BuildHome();
        Navigate("home");
    }

    private void AddNavigation(TableLayoutPanel grid, string key, string iconName, string es, string en, Action action)
    {
        var button = Translate(new NavigationButton(iconName), es, en);
        button.Width = 1;
        button.Dock = DockStyle.Top;
        button.Click += (_, _) => action();
        navigation.Add(key, button);
        AddRows(grid, button);
    }

    private void AddTheme(string key, string iconName, string es, string en)
    {
        var button = Translate(new NavigationButton(iconName) { IconsOnly = true, Width = 43, Height = 34 }, es, en);
        button.Click += (_, _) => SavePreferences(preferences with { Theme = key });
        themeButtons.Add(key, button);
        themeStrip.Controls.Add(button);
    }

    private void BuildHome()
    {
        var content = Grid(1);
        content.Padding = new Padding(28, 20, 28, 24);
        homePage.Controls.Add(content);

        var title = UiLabel("Inicio", "Home", 23, true);
        title.Margin = new Padding(0, 0, 0, 18);
        AddRows(content, title);

        var activity = new ShellCard();
        var presenceTitle = UiLabel("Actividad en Discord", "Discord presence", 14, true);
        var presenceHint = UiLabel("Consulta la actividad que la aplicación envía a Discord.", "See the activity the app sends to Discord.", 10);
        presenceHint.Tag = "muted";
        homeStatus.Tag = "muted";
        homeState.Tag = "muted";
        homeImages.Controls.AddRange([homeLarge, homeBadge]);
        AddRows(activity, presenceTitle, presenceHint, new ShellRule { Margin = new Padding(0, 12, 0, 8) },
            homeHeadline, homeDetail, homeState, homeImages, homeStatus);
        AddRows(content, activity);

        var fields = new ShellCard();
        var fieldsHint = UiLabel("Elige qué compartir desde Comportamiento y Ajustes de Discord.",
            "Choose what to share from Behavior and Discord settings.", 10);
        fieldsHint.Tag = "muted";
        AddRows(fields, UiLabel("Qué aparece en tu actividad", "What your activity shows", 14, true), fieldsHint,
            new ShellRule { Margin = new Padding(0, 12, 0, 4) });

        var table = Grid(2);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        AddFeatureRow("Información", "Information", "Cuándo aparece", "When it appears", true);
        AddFeatureRow("Carga y destino", "Cargo and destination", "Durante una entrega", "During a delivery");
        AddFeatureRow("Distancia restante", "Remaining distance", "Con una ruta en el GPS", "With a GPS route");
        AddFeatureRow("País y ciudad cercana", "Country and nearby city", "Con una ubicación estimada", "When location is estimated");
        AddFeatureRow("Conducción libre", "Free driving", "Sin una entrega activa", "Without an active delivery");
        AddFeatureRow("Temporizador", "Elapsed time", "Si está activado", "When enabled");
        AddRows(fields, table);
        AddRows(content, fields);

        var connection = new ShellCard();
        homeGame.Tag = "muted";
        var check = Translate(MakeButton(""), "Revisar conexión", "Check connection");
        check.Anchor = AnchorStyles.Left;
        check.Click += (_, _) => ShowConnectionGuide();
        var open = Translate(MakeButton(""), "Abrir ETS2", "Launch ETS2");
        open.Anchor = AnchorStyles.Left;
        open.Click += (_, _) => OpenExternal("steam://rungameid/227300");
        var tools = ToolBar();
        tools.Controls.AddRange([open, check]);
        AddRows(connection, UiLabel("Conexión con ETS2", "ETS2 connection", 14, true), homeGame, tools);
        AddRows(content, connection);
        homePage.ClientSizeChanged += (_, _) => ResizeHome();

        void ResizeHome()
        {
            var inner = homePage.ClientSize.Width - content.Padding.Horizontal - activity.Padding.Horizontal - 20;
            foreach (var label in new[] { presenceHint, homeHeadline, homeDetail, homeState, homeStatus, fieldsHint, homeGame })
                SetWrap(label, inner);
            foreach (Control child in table.Controls)
                if (child is Label label) SetWrap(label, inner / 2 - 8);
        }

        void AddFeatureRow(string name, string enName, string when, string enWhen, bool header = false)
        {
            var left = UiLabel(name, enName, header ? 9 : 10, header);
            var right = UiLabel(when, enWhen, header ? 9 : 10);
            if (header) left.Tag = "muted";
            right.Tag = "muted";
            left.Margin = right.Margin = new Padding(0, 7, 6, 7);
            var row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(left, 0, row);
            table.Controls.Add(right, 1, row);
            var rule = new ShellRule();
            AddRows(table, rule);
            table.SetColumnSpan(rule, 2);
        }
    }

    private void Navigate(string page)
    {
        currentPage = page;
        homePage.Visible = page == "home";
        tripShellPage.Visible = page == "trip";
        foreach (var (key, button) in navigation) button.SetSelected(key == currentPage);
        ResizeShell();
    }

    private void ResizeShell()
    {
        if (appShell.ColumnStyles.Count == 0) return;
        var scale = DeviceDpi / 96f;
        var collapsed = ClientSize.Width / scale < 750;
        appShell.ColumnStyles[0].Width = fullScreen ? 0 : (collapsed ? 56 : 200) * scale;
        if (narrowShell == collapsed) return;
        narrowShell = collapsed;
        foreach (var button in navigation.Values) { button.IconsOnly = collapsed; button.Invalidate(); }
        themeStrip.Visible = !collapsed;
        UpdateShellLabels();
    }

    private void ApplyShellTheme()
    {
        shellPalette = SystemInformation.HighContrast ? ShellPalette.System : preferences.Theme switch
        {
            "day" => ShellPalette.Day, "night" => ShellPalette.Dark,
            _ => Application.SystemColorMode == SystemColorMode.Dark ? ShellPalette.Dark : ShellPalette.Day
        };
        PaintShell(appShell);
        sidebar.BackColor = shellPalette.Surface;
        pageHost.BackColor = homePage.BackColor = scroll.BackColor = shellPalette.Canvas;
        travel.BackColor = preview.BackColor = gameStatus.BackColor = discordStatus.BackColor = shellPalette.Surface;
        if (truckName.Parent is { } truckCard) truckCard.BackColor = shellPalette.Canvas;
        foreach (var (key, button) in themeButtons) button.SetSelected(key == preferences.Theme);
        UpdateShellTooltips();
        UpdateShellLabels();
        Invalidate(true);
    }

    private void PaintShell(Control control)
    {
        switch (control)
        {
            case NavigationButton item:
                item.Palette = shellPalette;
                break;
            case ShellCard card:
                card.Palette = shellPalette;
                card.BackColor = shellPalette.Surface;
                break;
            case ShellRule rule:
                rule.Ink = shellPalette.Border;
                break;
            case Button button:
                button.BackColor = shellPalette.Surface;
                button.ForeColor = shellPalette.Text;
                button.FlatAppearance.BorderColor = shellPalette.Border;
                button.FlatAppearance.MouseOverBackColor = shellPalette.Hover;
                break;
            case Label label:
                label.ForeColor = Equals(label.Tag, "muted") ? shellPalette.Muted : shellPalette.Text;
                break;
            case ToolStrip strip:
                strip.BackColor = shellPalette.Canvas;
                strip.ForeColor = shellPalette.Text;
                break;
        }
        foreach (Control child in control.Controls) PaintShell(child);
    }

    private void UpdateShellTooltips()
    {
        foreach (var button in navigation.Values.Concat(themeButtons.Values))
            shellTips.SetToolTip(button, button.Text);
    }

    private void UpdateShellLabels()
    {
        var game = !hasShellStatus ? L("Buscando ETS2", "Checking ETS2")
            : shellGameOpen ? L("ETS2 abierto", "ETS2 running") : L("ETS2 cerrado", "ETS2 closed");
        var discord = !hasShellStatus ? L("Buscando Discord", "Checking Discord")
            : shellDiscordReady ? L("Discord conectado", "Discord connected") : L("Discord sin conexión", "Discord offline");
        sidebarGame.Text = narrowShell ? "●" : $"●  {game}";
        sidebarDiscord.Text = narrowShell ? "●" : $"●  {discord}";
        shellTips.SetToolTip(sidebarGame, game);
        shellTips.SetToolTip(sidebarDiscord, discord);
        sidebarGame.AccessibleName = game;
        sidebarDiscord.AccessibleName = discord;
        sidebarGame.ForeColor = shellGameOpen ? shellPalette.Accent : shellPalette.Muted;
        sidebarDiscord.ForeColor = shellDiscordReady ? shellPalette.Accent : shellPalette.Muted;
    }

    private void SyncHomeArt()
    {
        homeLarge.Image = previewLarge.Image;
        homeBadge.Image = previewBadge.Image;
        homeLarge.Visible = homeLarge.Image is not null;
        homeBadge.Visible = homeBadge.Image is not null;
        homeImages.Visible = homeLarge.Image is not null || homeBadge.Image is not null;
    }

    private void RenderHomePresence(PresenceText? shown, PresenceText? outgoing, string? age)
    {
        hasShellStatus = true;
        shellGameOpen = gameRunning;
        shellDiscordReady = discordReady;
        UpdateShellLabels();
        homeGame.Text = guidance.Text;
        homeHeadline.Text = shown is null ? L("Sin actividad publicada", "No activity published") : DiscordIdentity.ActivityName;
        homeDetail.Text = shown?.Details ?? "";
        homeDetail.Visible = shown is not null;
        homeState.Text = shown?.State ?? "";
        homeState.Visible = shown is not null && homeState.Text.Length > 0;
        var note = outgoing is not null ? (discordConfirmed ? L("Confirmada por Discord", "Confirmed by Discord")
            : L("Enviada a Discord", "Sent to Discord"))
            : shown is not null ? L("Vista previa local", "Local preview")
            : !preferences.Sharing ? L("Publicación en pausa", "Publishing paused")
            : L("La actividad aparecerá cuando ETS2 y Discord estén conectados.", "Activity appears when ETS2 and Discord are connected.");
        homeStatus.Text = age is null ? note : $"{note}  ·  {age}";
    }

    private void ShowAbout()
    {
        using var window = ToolWindow(L("Acerca de", "About"), new Size(520, 360));
        var content = Grid(1);
        content.Padding = new Padding(24);
        AddRows(content, TextLabel(Text, 18, true),
            TextLabel($"v{DesktopServices.Version.ToString(3)} · JesusCenMon", 10),
            TextLabel(L("Comparte tu actividad de Euro Truck Simulator 2 en Discord.", "Share your Euro Truck Simulator 2 activity on Discord."), 10));
        AddAction(content, "GitHub", () => OpenExternal("https://github.com/JesusCenMon/ETS2-Discord-Presence"));
        AddAction(content, L("Licencias", "Licenses"), ShowLicenses);
        window.Controls.Add(content);
        StyleTool(window);
        window.ShowDialog(this);
    }

    private sealed record ShellPalette(Color Canvas, Color Surface, Color Border, Color Text, Color Muted, Color Hover, Color Accent, bool Light)
    {
        public static ShellPalette Dark { get; } = new(Color.FromArgb(19, 22, 29), Color.FromArgb(24, 28, 37),
            Color.FromArgb(44, 50, 64), Color.FromArgb(240, 243, 249), Color.FromArgb(165, 179, 203),
            Color.FromArgb(36, 43, 58), Color.FromArgb(147, 186, 250), false);
        public static ShellPalette Day { get; } = new(Color.FromArgb(243, 246, 250), Color.White,
            Color.FromArgb(207, 216, 231), Color.FromArgb(25, 35, 54), Color.FromArgb(71, 88, 116),
            Color.FromArgb(226, 234, 248), Color.FromArgb(40, 88, 160), true);
        public static ShellPalette System => new(SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
            SystemColors.WindowText, SystemColors.WindowText, SystemColors.Control, SystemColors.HotTrack, isLight);
    }

    private sealed class ShellCard : TableLayoutPanel
    {
        public ShellPalette Palette = ShellPalette.Dark;

        public ShellCard()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnCount = 1;
            Dock = DockStyle.Top;
            Padding = new Padding(22, 15, 22, 17);
            Margin = new Padding(0, 0, 0, 18);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Palette.Canvas);
            if (Width < 4 || Height < 4) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 15);
            using var fill = new SolidBrush(Palette.Surface);
            using var border = new Pen(Palette.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }

    private sealed class ShellRule : Control
    {
        public Color Ink = ShellPalette.Dark.Border;

        public ShellRule()
        {
            Height = 1;
            Dock = DockStyle.Top;
            Margin = Padding.Empty;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(Ink);
    }

    private sealed class NavigationButton : Button
    {
        private readonly Bitmap icon;
        private readonly Font labelFont = new("Segoe UI", 10.5f);
        private bool selected;
        private bool hovered;
        public bool IconsOnly;
        public ShellPalette Palette = ShellPalette.Dark;

        public NavigationButton(string iconName)
        {
            using var stream = AppResources.Open($"fluent.{iconName}.png");
            using var bitmap = Image.FromStream(stream);
            icon = new Bitmap(bitmap);
            Font = labelFont;
            Height = 43;
            Margin = new Padding(0, 0, 0, 3);
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetSelected(bool value)
        {
            selected = value;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics;
            g.Clear(Palette.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (selected || hovered)
            {
                using var plate = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8);
                using var brush = new SolidBrush(Palette.Hover);
                g.FillPath(brush, plate);
            }
            var iconBox = IconsOnly ? ClientRectangle : new Rectangle(5, 0, 32, Height);
            var side = Math.Max(1, Math.Min(iconBox.Width, (int)Math.Round(21 * DeviceDpi / 96d)));
            var bounds = new Rectangle(iconBox.X + (iconBox.Width - side) / 2, (iconBox.Height - side) / 2, side, side);
            var ink = selected ? Palette.Accent : Palette.Muted;
            using var tint = new System.Drawing.Imaging.ImageAttributes();
            tint.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix
            {
                Matrix00 = 0, Matrix11 = 0, Matrix22 = 0,
                Matrix40 = ink.R / 255f, Matrix41 = ink.G / 255f, Matrix42 = ink.B / 255f
            });
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(icon, bounds, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, tint);
            if (!IconsOnly)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(43, 0, Math.Max(0, Width - 47), Height),
                    selected ? Palette.Text : Palette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3), Palette.Text, Palette.Surface);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                icon.Dispose();
                labelFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var size = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure();
        return path;
    }
}
