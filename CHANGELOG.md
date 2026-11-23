# Changelog

[Español](docs/CHANGELOG.es.md) · [Back to the project](README.md)

## v0.9.1 · 2026-11-23

- Preserve the established visual style while simplifying Home, reducing large headings and hiding the redundant trip strip on Home/Trip.
- Add explicit demo/replay notices and distinguish prepared, sent and acknowledged Discord activity.
- Protect unsaved Settings edits during navigation and exit with inline keep/discard/save choices; continue to the originally requested destination after saving.
- Add per-section undo, an unsaved-change count, native-theme restart guidance and inline save/startup errors.
- Group less frequent actions under Advanced options; settings search expands the relevant group automatically.
- Add a Journal detail pane, cell tooltips, clearer selection and selection-generation checks against asynchronous refresh.
- Keep replay transport outside the scrolling content and label playing, paused and finished states explicitly.
- Use buffered surfaces, grouped shell layout, unchanged-Home layout checks, short interaction deferral and scroll preservation.
- Make notice/footer layouts adapt to narrow windows and 200% text; retain maximized state after tray restoration.
- Add layout diagnostics and presentation/UI regression checks. Includes the unpublished v0.9.0 foundation below.

See [the 50-point refinement checklist](docs/QUALITY-0.9.1.md). Live game recognition and mixed-monitor DPI still require manual validation; automated fixtures do not establish universal latency guarantees.

## v0.9.0 · 2026-11-23

- Add bounded local navigation timings (cold/warm budgets), queue-stall monitoring and a review-before-save diagnostic report. No automatic upload or route/player/screenshot data is included.
- Prefetch up to 32 recording headers while idle, cancel prefetch on navigation and cache validated metadata.
- Filter/sort large Journals in cancellable background work with virtual cells. Persist filters, sort, selection and scroll in a separate local view-state file.
- Prepare replay graph bitmaps on a worker using immutable snapshots, cancelling superseded renders. Cursor drawing remains lightweight.
- Defer scheduled backup scanning/copying and retention cleanup. Skip hidden vehicle/dashboard rendering and shrink the artwork cache when minimized.
- Add inline Help with connection status, screen scan result/time, performance report, country/coach artwork preview and update controls.
- Confirm screen matches across two observations, tolerate transient misses and expire stale matches. Capture/OCR processing resumes off the UI thread; the photo reticle can work without OCR.
- Check activity artwork availability with bounded retry caching and use generic vehicle artwork when country artwork is unavailable.
- Extend interface/table text controls to 200%, normalize spacing and headings, retain native focus/contrast and reduced-motion behavior, and add recovery actions to empty states.
- Keep verified in-app downloads, previous-executable recovery and desktop Discord reconnection; expose update actions in Settings and Help.

Validation and practical limits are documented in [QUALITY-0.9.0.md](docs/QUALITY-0.9.0.md). Automated fixtures do not replace checking the detector in a live ETS2 session or testing actual mixed-DPI monitors.

## v0.8.8 · 2026-11-21

- Fix remaining delays when opening Settings and switching its tabs.
- Batch layout throughout each incoming control tree and release drawing once the page is ready.
- Prepare Settings styles before attaching native controls and skip repeated width/height measurements.
- Open the requested Settings section directly.
- Replace full-page movement with an atomic page update, retaining sidebar and button animations.
- Batch Trip resizing and reuse its rendered values when navigating with unchanged data.
- Restore keyboard focus after the drawing batch completes.

A visible-window local benchmark measured Settings opening at about 0.4–0.6 seconds, down from 3.3 seconds. Prepared settings tabs painted in approximately 16–30 ms. First Trip display measured 280 ms after fixing a separate resize cascade, with subsequent visits around 20 ms. Timings depend on hardware and content.

## v0.8.7 · 2026-11-21

- Retained Journal and Replays pages preserve selection, filters, scroll position and focus.
- Settings now opens inside the main window. Unvisited settings sections defer native control creation and layout.
- Page transitions use a short movement of the incoming page, replacing full-page bitmap captures.
- Journal cells are provided on demand. Unchanged data skips rebuilding rows, filters and statistics.
- Recording metadata is cached by file size and modification time. Search is delayed briefly while typing.
- Replay loading runs in the background, reports progress and supports cancellation.
- Replay rows, action menus, empty/error states and keyboard feedback are clearer.
- Artwork thumbnails have a bounded cache. Map drawing omits off-screen/subpixel segments, city lookup uses a spatial range, and labels try non-overlapping positions.
- Replay graphs preserve per-pixel extrema and redraw only the cursor strip during playback.
- Narrow settings layouts, large text, resize scheduling and shared animation resource reuse were improved.

Validation used a local 1,800-trip fixture, both interface languages, dark/light themes, 150% text, a narrow window, and 120 consecutive section changes per UI run. Warm Journal returns measured 24–27 ms in the final local benchmark, compared with 342–373 ms before. These timings describe this fixture and machine. Testing during a live ETS2 session and across monitors with different display scales remains pending.

## v0.8.6 · 2026-11-20

### Dashboard and navigation

- Home puts the current route and available distance, arrival, speed and fuel metrics first.
- The Current Trip bar remains visible in Journal, Replays and Settings and opens the Trip page.
- Trip brings the route and progress forward and hides metric tiles without data.
- Journal details, comparisons and replay playback open inside the main window.
- Replay recordings use two-line rows and include a favorites filter.

### Appearance and accessibility

- Shared graphite surfaces, warm text and configurable amber, blue and green accents.
- Rounded action buttons, animated setting switches and a text-size slider.
- Compact Settings search with keyboard focus and highlighted matches.
- Shared navigation, progress and state animations that respect reduced motion.
- Short status messages for settings, recording and marker actions.

### English and Spanish

- Fresh settings choose Spanish on Spanish-language Windows and English on other Windows languages.
- Interface and Discord activity language remain independently selectable.
- English project documentation, a Spanish guide and bilingual contribution templates.

### Changes since the last public release, v0.8.1

- Refined country artwork for truck and coach presence.
- Background loading for the recording library.
- Improved experimental photo recognition, including the controller viewfinder.
- Immediate publishing requests after a confirmed activity change.

### Validation and known limits

The release build and automated tests are checked during packaging. Visual checks use local demonstration data. Live Discord display, actual driving, mixed-DPI monitor transitions and experimental map/photo detection still need coverage across player setups.

## Earlier releases

See the [Spanish release history](docs/CHANGELOG.es.md) for v0.8.5 through earlier versions and the [GitHub releases](https://github.com/JesusCenMon/ETS2-Discord-Presence/releases) for previously published downloads.
