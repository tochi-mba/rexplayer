# Windows library and shell: implementation evidence and remaining acceptance

Updated 2026-10-23 in follow-up PR #488. This is an evidence map, not a marketing list or proof that the branch is release-ready.

## Source behavior that exists now

| Feature / area | What the code currently does | Source and tests | Unverified or separate acceptance |
| --- | --- | --- | --- |
| Music Home | Builds a local featured item, recent albums, listening-based rows and mixes; pins and unpins real albums by persisted representative file paths; omits missing pins | `MusicHome.Rows`, `MainWindow.MusicHome`, `MainWindow.AlbumPage`, `PlayerSettings.PinnedAlbumPaths`; `MusicHomeTests`, `LibraryMusicTests` | Native keyboard/scroll/high-DPI performance, images and screenshot review |
| Songs, artists, albums, genres | Sorts and groups by actual tags, handles mixed artist credits, independent album identities, disc/track order and unknown tag fallbacks | `LibraryViews`, `LibraryArrangement`, `ArtistNames`, `AlbumPage`, `GenreNames`; `LibraryArrangementTests`, `AlbumPageTests`, `ArtistNamesTests`, `GenreNamesTests` | Wide/massive libraries, consistent density/virtualization and all native accessibility states |
| TV browsing | Keeps movies distinct from seasons and episodes, orders seasons and specials, shows honest completion for episode identities instead of counting duplicate resolutions as separate episodes, preserves current season route on index refresh | `LibraryViews.TvShows/TvSeasons`, `EpisodeNames`, `ShowPage`, `MainWindow.ShowPage`, `MainWindow.DetailsParts`; `EpisodeNamesTests`, `ShowPageTests`, `LibraryFoldersTests` | Actual screenshot state/error review and current-head native CI |
| Library resilience | Runs scans on background workers; an inaccessible root/subdirectory does not masquerade as deleted media; preserves last-played history in an absent-entry store and restores it for a returning file or a safely matched moved path | `LibraryScanner`, `MediaLibrary`; `LibraryScannerTests`, `MediaLibraryTests` | Real device unplug/replug, network shares, multi-volume move and error-state screenshots |
| Restore and backup | Produces a versioned local JSON export and M3U8 playlists; stages all files and digests then publishes a new snapshot folder atomically, verifies SHA-256 contents before restore, and asks for confirmation after a read-only additive-import preview | `LibraryBackup.Export/PublishSnapshot/ReadFromFile/Preview/Restore`, `MainWindow.Library`; `LibraryBackupTests` | Full cross-store atomic restore and rollback **not implemented**; legacy JSON has no manifest; native recovery review |
| Metadata and artwork | Keeps online lookup switches off until selected; blocks excluded paths, validates number/date edits; explains where fields came from; queues tag writes safely; previews selected covers including image format, original dimensions and aspect | `MetadataPolicy`, `DetailsForm`, `PendingDetailWrites`, `MainWindow.EditDetails`, `MainWindow.Metadata`; `MetadataPolicyTests`, `DetailOverrideTests`, `PendingDetailWritesTests` | Native preview/keyboard/screen reader checks and full network permission audit |
| Lyrics and visualizers | Uses local embedded or sidecar lyrics only; line navigation can suspend auto-follow; exact supplied enhanced-LRC word positions drive karaoke color; visualizer options are isolated by mode, and each native control has an automation label | `Lyrics`, `MainWindow.Presentation`, `VisualizerOptions`, `MainWindow.VisualizerSettings`; `PresentationTests`, `VisualizerGroupsTests`, `AppWindowTests` | Detached lyrics companion, complete lyric empty/error states, continuous rate/seek/resize validation, stress and screenshot checks |
| Windows shell and Home | Uses the existing shell, routes, view model and data-driven Home templates instead of an invented design. The merged follow-up does not claim every minimum-width, high-contrast and controller state has been verified | `MainWindow.xaml`, `MainWindow.LibraryView`, `MainWindow.Sections`, `MainWindow.DetailsParts`, `HomeShelves`; library UI and shell test projects | Compact/wide/high-DPI/high-contrast, narration, controller and snapshot review |

## Follow-up implementation and site checks (2026-10-24)

- **Temporary compact navigation:** `MainWindow.LibraryNavigation.CollapseTemporaryLibraryNavigation` gives the routed content its width back when someone selects a destination from a temporarily expanded narrow sidebar. `MainWindow.LibraryOpening.OnLibrarySourceChanged` invokes it without changing the saved wide-window setting. `LibraryFixtureVisualTests.NarrowNavigationReturnsContentWidthAfterAViewIsChosen` checks the 720px -> 1000px interaction and keeps the chosen Songs route stable. This test is committed, **not claimed as run on Windows**; minimum-size/DPI/high-contrast/controller review remains outstanding under #486.1/#486.4/#486.7/#486.8.
- **Screenshot walkthrough quality:** `scripts/check_site.py` now validates every `data-tour-src` and nonempty `data-tour-alt`, not only images initially in HTML. `tests/site/test_check_site.py` covers missing/undescribed dynamically selected images; `tests/site/pages.spec.js` decodes each source and checks accessible alt text and no horizontal overflow at 320px and 1280px. These are **proposed automated verification on the PR head**, not reviewed finalized Windows/Mac release captures or deployed GitHub Pages evidence. All #468 release/deployment gates remain open.
- **Documentation scope (#481.12 and #486.9):** this source-level evidence map, [Windows foundation and QA](01-foundation-and-qa.md), and [music/lyrics/visualizer workstream](03-music-lyrics-visualizers.md) describe only paths and focused tests that can be identified in the branch. `docs/capability-matrix.json` and generated `docs/parity.md` have **not** been promoted to claim additional verified features. Platform/native claims need their separate acceptance evidence.

## Unified Windows Browse behavior (#486.3, 2026-10-24)

`MainWindow.LibraryView.BuildLibrary` already handles shared navigation, per-route `LibraryViewChoice` persistence, search and quick filtering before dispatching to List, Cards, Wall, Collage or Browse; `MainWindow.LibraryBrowse` builds shelves with the shared virtualized `MainWindow.LibraryView.Shelf` component and common source headings. The follow-up corrects two inconsistencies in **Movies and TV Shows**, rather than introducing a second browsing model:

- `ShowStreamingBrowse` now forwards the active `LibraryViewChoice` to `ShowLibraryEmpty` so a Browse filtered to zero titles uses the same specific explanation and `LibraryEmptyClearFilters` action as every other layout. `LibraryBrowseConsistencyTests.AFilteredEmptyBrowseOffersClearFiltersAndRestoresContent` includes Movies and TV Shows scenarios and fixture screenshot calls.
- The **All movies** shelf now applies `LibraryArrangement.Sort` to the current `LibraryViewChoice` (including Reverse); **All shows** uses the same shared group sorter. Purpose-specific Continue watching / Recently added / Most watched / genre shelves retain their own documented ordering instead of being disrupted by the global sort setting. This preserves useful curated browsing while making the all-items shelf behave like the other layouts.
- Existing routes, `LibraryPlaces` anchor restoration, keyboard search, `LibraryArrangement` filters, UIA-named shelf controls and list/card templates remain shared. **Focus fix:** `MainWindow.LibraryEmpty.ShowLibraryEmptyState` now returns focus to the surviving LibrarySearch or LibraryFilter control after its ephemeral Clear search / Clear filters button is removed on reload. `LibraryBrowsingTests.FiltersSayWhenTheyEmptyAViewAndSearchClearsOnEscape` and `LibraryBrowseConsistencyTests.AFilteredEmptyBrowseOffersClearFiltersAndRestoresContent` assert the resulting native UIA keyboard focus. Touch, controller and actual assistive-technology testing remain open under #486.4. Existing `LibraryFoldersTests`, `LibraryFixtureVisualTests` and `LibraryArrangementTests` cover the unchanged contracts.

These are source and authored regression-test evidence for closing the **unification implementation task** #486.3. Windows UI execution, accessibility on actual input devices, screenshots at high contrast/DPI and performance gates remain separately **unchecked** under #486.4/#486.7/#486.8.

## Additional authored TV/movie capture coverage (#479.7)

`LibraryFixtureVisualTests.DisconnectedLibraryFolderShowsRecoveryInsteadOfAFalseEmptyClaim` requests a native rescan after a watched root vanishes and captures the actual error with reconnect guidance. The pure `LibraryEmptyState.For` now exposes `OffersRescan` specifically for idle scan errors; `MainWindow.LibraryEmpty.ShowLibraryEmptyState` displays a named `LibraryEmptyRetryScan` button that retries without deleting the remembered library. The native fixture checks the accessible name and the result of a failed retry; `LibraryEmptyStateTests.FailedLibraryScanNeverClaimsThatTheDriveIsEmpty` checks error vs scanning vs filtered-empty. `LibraryDownloadsTests.AGrowingVideoWearsTheBadgeUntilWholeAndTheFilterAndShelfFollowIt` and `PlayOnAFileTooShortToOpenSaysHowFarItsDownloadIs` now capture partial-download status and graceful playback refusal. Together with the existing empty/populated Home, Movie and TV captures, the authored fixtures span empty, populated, partial and error states. They remain **record-only until run on Windows and reviewed**; #479.7/#479.8 native screenshot acceptance is not marked complete.

## Native macOS library and accurate Windows About messaging (2026-10-24)

- **macOS implementation for source task #468.8:** `src/Rex.Media.App.Mac/MainWindow.Library.cs` now creates a genuine AppKit library browser backed by the existing on-disk `MediaLibrary` and `LibraryScanner`: user-chosen watched folders, asynchronous indexing, local music/video/picture/folder filters, shared Unicode-aware `LibraryViews.Search`, paginated controls and Play/Add to Up Next operations. Its native entry points are wired from `MainWindow.cs` including the menu and welcome screen. `MacAppSmokeTests` exercises sample MP3/MP4 indexing, search, filtering, playback, watched-folder removal without deleting media, and native screenshot capture, and `.github/workflows/ci.yml` uploads separate `mac-library-screenshots` for inspection. Captures are labeled **test fixture evidence only** with build provenance, not final-release media. Neither a compiled/current-head macOS smoke pass nor VoiceOver/multi-display/human pixel review has been obtained in this edit, so #468.8 remains unchecked and no parity status is elevated.
- **Windows About correction for source task #485.9:** `MainWindow.About.cs` no longer claims absolutely that the application sends no data; it explains that local media requires no account and that optional metadata lookups and update checks can use the network. A stable UIA `AboutPrivacy` name and `AppWindowTests.AboutNamesTheVersionAndWhereTheSettingsLive` assert the explanation. The full preferences, updater, dialogs and shell acceptance matrix remains open.

## Native Mac search, paging and watched-folder refresh (2026-10-24)

- `MainWindow.Library` now accepts Return in the native search text field, presents an explicit Clear search control and tells the user when the current filter matches nothing. The restricted Mac smoke actions check real empty-result → reset → restored browse behavior, an additional native capture, and accessible labels.
- The Folders route skips media existence checks and sorts, so opening watched locations is not blocked by thousands of media-file probes on the AppKit thread. The `LibraryPageSlice.For` pure helper clamps missing/stale pages, handles 60/61/121 records and near-`int.MaxValue` counts without integer overflow; `LibraryPageSliceTests` defines the boundary contracts.
- A five-minute `NSTimer` triggers incremental scans for nonempty watch lists even without a manual Rescan press, and is invalidated during Mac teardown. The menu invokes `CommandCatalog.ToggleLibrary` through the common keymap, preserving the configured Mac shortcut. This is **periodic** watching, not a claim of instant filesystem-event delivery or certified parity with Windows.
- The native Mac app has **not** been built or screen-reader reviewed at this head; CI fixture PNGs remain test evidence, not publication-ready macOS release images. Task #468.8 stays open.

## Windows Home source fixture additions (2026-10-24)

- `LibraryFixtureVisualTests.HomeNavigationRemainsReachableAcrossNativeWindowWidths` provides source-backed 360×430 minimum, 720×640 compact, 1000×760 standard and 1280×860 wide Home screenshot/test cases. Each asserts the live navigation and usable content width before attempting a capture.
- `LibraryMusicTests.MusicHomePinsOnlyRealAlbumsAndAllowsRemovingThem` and `UnifiedHomeShowsOnlyAlbumsTheUserActuallyPinned` now capture the actual pinned-album shelf after native assertions, avoiding fabricated pinned media. Existing empty, populated, disconnected, RTL, search/filter, TV and partial-download fixtures remain.
- Current-head WinUI execution, DPI/contrast, controller/screen-reader review and approved snapshot comparisons remain **open under #486.1/#486.4/#486.7/#486.8**. Adding a `Screenshots.Match` invocation does not prove that a PNG was captured or visually approved.

## Native TV/movie screenshot-automation authoring completed (#479.7, 2026-10-24)

Native UI Automation has now been **authored** for all four requested library screenshot-state categories:

| Requested state | Screenshot-producing native test and data/UIA checks |
| --- | --- |
| Empty | `LibraryFixtureVisualTests.EmptyWatchedLibraryShowsARealEmptyState`; Movie/TV Browse filtered-empty in `LibraryBrowseConsistencyTests.AFilteredEmptyBrowseOffersClearFiltersAndRestoresContent` |
| Populated | `LibraryFoldersTests.SeasonFoldersMakeShowsWhereverTheyLiveAndMoviesStayApart` (TV billboard, film, show, season two), `ASpecialBesideASeasonFolderJoinsItsShow` (specials) and featured Browse fixture |
| Partial | `LibraryDownloadsTests.AGrowingVideoWearsTheBadgeUntilWholeAndTheFilterAndShelfFollowIt` (progress and download badge), `PlayOnAFileTooShortToOpenSaysHowFarItsDownloadIs` (native graceful refusal) |
| Error | `LibraryFixtureVisualTests.DisconnectedLibraryFolderShowsRecoveryInsteadOfAFalseEmptyClaim` (unreadable/disconnected root, retry button and no false empty claim) |

`AppProcess.StartInfo` now enables `REXPLAYER_UI_CAPTURE=1` for real native UI test sessions. `MainWindow.LibraryBrowse` respects that switch only to freeze the rotating feature and fade animation; no mock cards or fabricated photos replace the actual native renderer. `Screenshots.Match` records named PNGs to the test artifact folder; a missing baseline is explicitly **record-only**. Thus #479.7's *automation/capture code-authoring* work can be marked implemented, but this does **not** demonstrate a successful current-head Windows run, approved screenshot baselines or high-DPI/accessibility performance. Those distinct gates remain in #479.8 and #468.

## Prevent action-bubbling in native Movie/TV features (2026-10-24)

Previously `MainWindow.LibraryBrowse.Billboard` handled `Tapped` on the whole card without excluding its nested Play, More info and Wikipedia buttons. Routed pointer taps on those controls could therefore also call `more()`. The handler now traverses the actual WinUI visual ancestors of `args.OriginalSource` and declines any action already owned by a nested `ButtonBase`; a background tap still opens the correct details view. The two explicit buttons have informative, title-specific accessibility names.

`LibraryBrowseConsistencyTests.BrowseHeroPlayAndMoreInfoRemainIndependentNativeActions` sends **real mouse events** with `Pointer.Click`, which exercise routed `Tapped` propagation that `InvokePattern` alone would not cover. The test navigates More info → Back, then uses Play and requires the native player (not a detail panel) to appear. This is authored WinUI regression coverage, not a claimed latest-head native result.

## Windows high-contrast Movie/TV feature banners (2026-10-24)

`MainWindow.LibraryBrowse.Billboard` previously painted a dark photographic gradient under hard-coded white/muted text, irrespective of the OS high-contrast palette. It now reads `AccessibilitySettings.HighContrast`, uses `UISettings.GetColorValue(Foreground/Background)` for the banner in high contrast, and omits photo/backdrop requests and fade-in animation when those preferences apply. The pure `BrowseHeroPresentation.For`/ `BrowseHeroPresentationTests.SystemContrastOverridesPhotographyAndFades` covers all four combinations of animation and high contrast. `MainWindow.Library.WireLibrary` invalidates rendered library surfaces when the XAML theme changes, rather than leaving stale photographic surfaces onscreen. `LibraryBrowseConsistencyTests.AFilteredEmptyBrowseOffersClearFiltersAndRestoresContent` also captures the native featured Movie/TV billboards and checks their action/UIA controls before applying filters.

These changes give a tested *source-level rendering policy*, **not a claimed successful Windows high-contrast visual pass or live theme-switch verification**. The native pixel/screen-reader checks and #486.1/#486.7/#486.8 remain open.

## Shell and Home screenshot fixture-authoring coverage (#486.7, 2026-10-24)

All shell/Home states changed on this branch now have **source-authored native UI Automation fixtures** backed by genuine offline media, not drawings or fixture-free assertions:

| Changed native shell/Home state | Named screenshot source and native data/visibility assertions |
| --- | --- |
| First-run empty/Light/Dark, Settings at standard/compact | `AppWindowTests.Screenshots.TheEmptyWindowLooksTheSameInEachTheme` and `PreferencesLooksTheSameAtEachSize` |
| Home empty and folder recovery error | `LibraryFixtureVisualTests.EmptyWatchedLibraryShowsARealEmptyState` and `DisconnectedLibraryFolderShowsRecoveryInsteadOfAFalseEmptyClaim` |
| Populated mixed-media Home, wide and compact | `LibraryFixtureVisualTests.MixedOfflineLibraryHasNavigableShelvesInWideAndCompactWindows` |
| Minimum, compact, standard, wide navigation and Home | `LibraryFixtureVisualTests.HomeNavigationRemainsReachableAcrossNativeWindowWidths`: 360×430, 720×640, 1000×760 and 1280×860 |
| Compact temporary expanded rail → route restores content width → desktop preference restored | `LibraryFixtureVisualTests.NarrowNavigationReturnsContentWidthAfterAViewIsChosen` with three additional real routed-state screenshot calls |
| Genuine pinned album Music Home and unified Home | `LibraryMusicTests.MusicHomePinsOnlyRealAlbumsAndAllowsRemovingThem` and `UnifiedHomeShowsOnlyAlbumsTheUserActuallyPinned`, after checking actual pinned album rows |
| High-contrast Home and empty Movie view | `LibraryFixtureVisualTests.HighContrastHomeUsesLiveSystemPaletteAndRetainsAccessibleNavigation`; only runs when OS high contrast is **really enabled** |
| Per-monitor high-DPI Home | `LibraryFixtureVisualTests.HighDpiHomeKeepsNavigationReachableWithoutASecondLayout`; checks Win32 `GetDpiForWindow` at 120 DPI or higher, then verifies accessibility/layout |
| Search/filter empty, Unicode/RTL song row, one-item library | `LibraryBrowsingTests.FiltersSayWhenTheyEmptyAViewAndSearchClearsOnEscape` and `LibraryFixtureVisualTests.UnicodeAndRtlNamesSurviveOfflineIndexingAndRemainFocusable` / `OneItemLibraryKeepsTheSongBrowsableWithoutOnlineMetadata` |

`AppProcess.StartInfo` sets `REXPLAYER_UI_CAPTURE` to hold native Browse heroes steady in screenshot automation. `Screenshots.Match` writes actual PNGs when WinUI tests run; named high-contrast/DPI fixtures *skip* rather than simulating a system setting that is not present. This **completes the source-level fixture authoring in #486.7**, but **does not certify a current-head Windows capture, snapshot approval, DPI/contrast acceptance, accessibility device pass, or performance**. Those are still open and separately enumerated in #486.1/#486.4/#486.8 and #468.

## Capabilities and platform parity must stay truthful

The source of truth for shipping declarations is `docs/capability-matrix.json`. The currently declared Windows `LIB-05`, `LIB-10`, `META-09` and `AU-18` statuses come from their existing functional tests and must not be changed simply because a follow-up task has source code. macOS is **not** labeled as having equivalent Windows library, visualizer, lyric or shell UI work in this document. `docs/parity.md` is generated and is not hand-edited.

No release screenshots were fabricated or relabeled. Mac library and Up Next screenshots remain blocked on native feature/build verification, and the public website must still be checked after deployment on real desktop/mobile viewports. Those gates are recorded in source tasks #468.4-#468.10 and #486.7.

## Change acceptance / handoff policy

A source behavior can be documented once it has a concrete implementation and focused tests. A broader quality gate must remain unchecked until **current-head** Windows build, portable tests, native UI, accessibility, performance, screenshot review and any real-site checks relevant to that gate pass. Older successful runs do not cover the latest unverified follow-up commits. [Follow-up PR #488](https://github.com/tochi-mba/rexplayer/pull/488) and `merged-pr-followup-audit.md` hold the per-requirement checkbox ledger.

This file fulfills evidence-based documentation work, not the remaining implementation or native approval itself.
