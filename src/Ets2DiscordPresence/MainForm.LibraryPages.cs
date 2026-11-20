namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private readonly Stack<LibraryPage> libraryPages = new();

    private sealed class LibraryPage : Panel
    {
        internal readonly bool PlaysRecording;
        internal Action? Leaving;
        internal Control? ReturnFocus;
        internal Func<Keys, bool>? HandleShortcut;

        internal LibraryPage(string title, bool playsRecording = false)
        {
            Text = AccessibleName = title;
            PlaysRecording = playsRecording;
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData) =>
            HandleShortcut?.Invoke(keyData) == true || base.ProcessCmdKey(ref message, keyData);
    }

    private Button BackButton()
    {
        var back = MakeButton(L("← Volver", "← Back"));
        back.Tag = "quiet";
        back.Click += (_, _) => GoBackInLibrary();
        return back;
    }

    private void PushLibraryPage(LibraryPage page)
    {
        if (libraryPages.Count > 0 || currentPage is "home" or "trip") AnimatePageChange(pageHost);
        homePage.Visible = tripShellPage.Visible = false;
        page.Visible = false;
        if (libraryPages.TryPeek(out var previous))
        {
            previous.ReturnFocus = FindFocusedControl(previous);
            previous.Hide();
        }
        libraryPages.Push(page);
        page.BackColor = shellPalette.Canvas;
        pageHost.Controls.Add(page);
        page.BringToFront();
        page.Show();

        page.SelectNextControl(null, true, true, true, false);
        UpdatePageTitle();
        UpdateReplayVisibility();
    }

    private void GoBackInLibrary()
    {
        AnimatePageChange(pageHost);
        PopLibraryPage();
        if (libraryPages.TryPeek(out var previous))
        {
            previous.Show();
            previous.BringToFront();

            if (previous.ReturnFocus?.Focus() != true)
                previous.SelectNextControl(null, true, true, true, false);
        }
        else
        {
            homePage.Visible = currentPage == "home";
            tripShellPage.Visible = currentPage == "trip";
            if (navigation.TryGetValue(currentPage, out var link)) link.Focus();
        }
        UpdatePageTitle();
        UpdateReplayVisibility();
    }

    private static Control? FindFocusedControl(Control parent)
    {
        if (!parent.ContainsFocus) return null;
        return parent.Controls.Cast<Control>().FirstOrDefault(child => child.ContainsFocus) is { } child
            ? FindFocusedControl(child) : parent;
    }

    private void PopLibraryPage()
    {
        if (!libraryPages.TryPop(out var page)) return;
        page.Leaving?.Invoke();
        page.Dispose();
    }

    private void ClearLibraryPages()
    {
        while (libraryPages.Count > 0) PopLibraryPage();
    }

    private void UpdateReplayVisibility()
    {
        var wasPlaying = replayOpen;
        replayOpen = libraryPages.TryPeek(out var page) && page.PlaysRecording;
        if (wasPlaying != replayOpen && !IsDisposed && !Disposing)
        {
            publishImmediately = true;
            RefreshPresence();
        }
    }

    private void UpdatePageTitle()
    {
        var title = libraryPages.TryPeek(out var page) ? page.Text : currentPage switch
        {
            "trip" => L("Viaje", "Trip"),
            "journal" => L("Diario", "Journal"),
            "replay" => L("Repeticiones", "Replays"),
            _ => L("Inicio", "Home")
        };
        Text = $"ETS2 Discord Presence · {title}";
    }
}
