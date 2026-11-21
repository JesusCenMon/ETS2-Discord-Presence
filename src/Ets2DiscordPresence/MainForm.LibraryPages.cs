namespace Ets2DiscordPresence;

internal sealed partial class MainForm
{
    private Stack<LibraryPage> libraryPages = new();
    private readonly Dictionary<string, Stack<LibraryPage>> retainedPages = [];
    private string? requestedPage;
    private bool navigationQueued;

    private string PageContext => string.Join("|", preferences.Language, preferences.TextPercent,
        preferences.Features.TableTextPercent, preferences.Features.Miles);

    private sealed class LibraryPage : Panel
    {
        internal readonly bool PlaysRecording;
        internal Action? Leaving;
        internal Action? Activated;
        internal Action? Deactivated;
        internal Control? ReturnFocus;
        internal Func<Keys, bool>? HandleShortcut;
        internal string Context = "";

        internal LibraryPage(string title, bool playsRecording = false)
        {
            Text = AccessibleName = title;
            PlaysRecording = playsRecording;
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            TabStop = false;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData) =>
            HandleShortcut?.Invoke(keyData) == true || base.ProcessCmdKey(ref message, keyData);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                Deactivated?.Invoke();
                Deactivated = null;
                Leaving?.Invoke();
                Leaving = null;
            }
            base.Dispose(disposing);
        }
    }

    private void QueueNavigation(string page)
    {
        requestedPage = page;
        foreach (var (key, link) in navigation) link.SetSelected(key == page);
        if (navigationQueued) return;
        navigationQueued = true;
        BeginInvoke(() =>
        {
            navigationQueued = false;
            if (IsDisposed || requestedPage is not { } target) return;
            requestedPage = null;
            Navigate(target);
        });
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
        using var redraw = new UiRedrawScope(pageHost);
        homePage.Visible = tripShellPage.Visible = false;
        page.Visible = false;
        if (libraryPages.TryPeek(out var previous))
        {
            previous.ReturnFocus = FindFocusedControl(previous);
            previous.Deactivated?.Invoke();
            previous.Hide();
        }
        page.Context = PageContext;
        libraryPages.Push(page);
        page.BackColor = shellPalette.Canvas;
        pageHost.Controls.Add(page);
        page.BringToFront();
        page.Show();
        RestorePageFocus(page);
        UpdatePageTitle();
        UpdateReplayVisibility();
    }

    private static void RestorePageFocus(LibraryPage page)
    {
        if (!page.IsHandleCreated) return;
        page.BeginInvoke(() =>
        {
            if (page.IsDisposed || !page.Visible) return;
            if (page.ReturnFocus is { IsDisposed: false, CanFocus: true } focus) focus.Focus();
            else page.SelectNextControl(null, true, true, true, false);
        });
    }

    private void GoBackInLibrary()
    {
        if (libraryPages.Count <= 1) { Navigate("home"); return; }
        using var redraw = new UiRedrawScope(pageHost);
        PopLibraryPage();
        if (libraryPages.TryPeek(out var previous))
        {
            previous.Show();
            previous.BringToFront();
            previous.Activated?.Invoke();
            RestorePageFocus(previous);
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
        if (libraryPages.TryPop(out var page)) page.Dispose();
    }

    private void RetainLibraryPages()
    {
        if (libraryPages.Count == 0) return;
        // Playback owns timers and a potentially large recording. Stop it when leaving its section.
        while (libraryPages.Count > 1) PopLibraryPage();
        var root = libraryPages.Peek();
        root.ReturnFocus = FindFocusedControl(root);
        root.Deactivated?.Invoke();
        root.Hide();
        retainedPages[currentPage] = libraryPages;
        libraryPages = new();
    }

    private bool RestoreLibraryPages(string page)
    {
        if (!retainedPages.Remove(page, out var saved)) return false;
        if (saved.Count == 0) return false;
        if (saved.Peek().IsDisposed || saved.Peek().Context != PageContext)
        {
            foreach (var view in saved) view.Dispose();
            return false;
        }
        libraryPages = saved;
        var root = saved.Peek();
        using var layout = new UiLayoutBatch(root);
        root.Show();
        root.BringToFront();
        layout.Dispose();
        root.PerformLayout();
        root.Activated?.Invoke();
        RestorePageFocus(root);
        return true;
    }

    private void ClearLibraryPages()
    {
        while (libraryPages.Count > 0) PopLibraryPage();
        foreach (var stack in retainedPages.Values)
            foreach (var page in stack) page.Dispose();
        retainedPages.Clear();
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
            "settings" => L("Ajustes", "Settings"),
            _ => L("Inicio", "Home")
        };
        Text = $"ETS2 Discord Presence · {title}";
    }
}
