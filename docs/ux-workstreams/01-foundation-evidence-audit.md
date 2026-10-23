# Windows foundation evidence audit for PR #470

This is a source and test inventory, not approval of an unobserved desktop layout. It records what existed at the start of the workstream and where acceptance evidence must come from. The canonical task list is the PR description and `01-foundation-and-qa.md`.

## Immutable starting snapshot and traceability

- Initial PR head: `a21bbbfd8eaf4fa0b8265ccd9e89436fb9a4fa53`, against `main` at `6ac20abf6f448f2d350d177860e0934589a94120`.
- PR #467 head: `d176f95017cd5fd882a9cb91494b608c6a1c10ba`. Its six changed files were `CHANGELOG.md`, `MainWindow.Preferences.cs`, `AppWindowTests.Preferences.cs`, `AppWindowTests.Screenshots.cs`, `Screenshots.cs` and `ScreenshotsTests.cs`, under their respective source/test directories. All six were carried into this branch.
- Review threads and comments on #467 and #470 at the initial audit: none returned by GitHub.
- Original head CI: [run 39993569880](https://github.com/tochi-mba/rexplayer/actions/runs/39993569880). The Windows, Linux and macOS pure-test jobs failed `RepositoryTests.NoTrackedTextContainsAnEmDash` because this workstream document contained prohibited punctuation. The initial run's Windows UI job was cancelled; it does not prove UI correctness. The rule failure was fixed in commit `3f2ca6af629c48bfc1cea4d3f4be546ca69226e7`. Its subsequent CI must be checked independently.
- Original head built both macOS app architectures and passed website and Windows performance jobs; this does not prove parity or the new head.
- Read `AGENTS.md` and `docs/adr/0020-parity-one-ledger-one-release.md`. Native shell work does not imply macOS parity; only the status and tests in `docs/capability-matrix.json` decide. No capability status changes are asserted by this audit.
- Screenshot automation was already present before this workstream: `Screenshots.Match`, two committed CI Windows baselines, the blank/uniform image preflight, and 48 current screenshot scene variants found in UI tests. Screenshot inventory is now `tests/screenshot-scenes.json`; an unbaselined scene records evidence without passing a pixel comparison.

## Windows surface entry and proof map

A row describes the current route or control from source and tests. A scene ID describes an existing screenshot capture, not necessarily an approved visual baseline. `Unbaselined` and `needs review` are deliberate blockers.

| Surface | Entry and current source owner | Capability and existing functional coverage | Screenshot scene or acceptance gap |
| --- | --- | --- | --- |
| Idle / initial window / playback | Launch and transport commands; `MainWindow.xaml`, `MainWindow.Launch.cs`, `MainWindow.Input.cs`, `MainWindow.xaml.cs` | `UI-01`, `UI-13`; `AppWindowTests.Launching`, `AppWindowTests.Transport` | `empty-window-light`, `empty-window-dark`, `playback-paused`; all need approved baseline |
| Home / library shell | Ctrl+Shift+L, Welcome library button; `MainWindow.Library.cs`, `MainWindow.LibraryNavigation.cs` | `LIB-05`; `PlayerExperienceTests`, `LibrarySidebarTests` | `experience-library-wide-light`, `experience-library-wide-dark`; compact/RTL/high-contrast evidence missing |
| Music and Home shelves | Library sidebar music; `MainWindow.MusicHome.cs`, `MainWindow.LibraryBrowse.cs`, `MainWindow.LibraryHero.cs` | `LIB-05`; `LibraryMusicTests`, `LibraryShelfTests` | `music-home`; Home empty/music-only/mixed captures missing |
| Songs, Albums, Artists, Genres | Library sidebar and card details; `MainWindow.LibraryView.cs`, `MainWindow.LibraryGroups.cs`, `MainWindow.AlbumPage.cs`, `LibraryMusicTemplates.xaml` | `LIB-05`; `LibraryBrowsingTests`, `LibraryArtistsTests`, `LibraryMusicTests` | `songs-table`, `album-page`, `library-songs-collage`; search persistence and high-DPI evidence missing |
| Audiobooks, Authors, Podcasts, Recordings | Library source switch; `MainWindow.Listening.cs`, `MainWindow.ListeningActions.cs` | `LIB-05`; `LibraryListeningTests` | Dedicated visual family not yet recorded; coordinate specialist content with #473 |
| Movies, TV Shows, Other videos, All videos | Library source switch, group and season details; `MainWindow.OtherVideos.cs`, `MainWindow.ShowPage.cs`, `MainWindow.LibraryDetails.cs` | `LIB-05`; `LibraryOtherVideosTests`, `LibraryDetailsTests`, `LibraryFoldersTests` | `library-tv-browse`, `library-show-page`, `library-movie-details`, `library-show-header`; dedicated ratios `library-movie-card-letterboxed` and `library-episode-still-letterboxed` have committed baselines |
| Pictures | Library sidebar picture grid; `MainWindow.Pictures.cs`, `LibraryCardTemplates.xaml` | `LIB-05`; `LibraryPictures` | Picture grid and missing-artwork captures pending; avoid duplicating #471 content |
| Continue Watching, Completed, Recently Played, Recently Added | Library source list and history; `MainWindow.Completed.cs`, `MainWindow.Library.cs` | `LIB-05`, `LIB-07`; `RecentlyPlayedTests` and browsing tests | Status and short-window visual proof missing |
| Playlists, Folders | Library source list and watched-folder actions; `MainWindow.Playlists.cs`, `MainWindow.LibraryFolders.cs`, `MainWindow.NamedPlaylists.cs` | `LIB-04`, `LIB-05`, `LIB-10`; `LibraryFoldersTests`, `LibraryPlaceTests` | Folder permission/disconnect and breadcrumbs screenshots pending |
| Queue / transport overlays / fullscreen / minimal | `MainWindow.xaml`, `MainWindow.Presentation.cs`, `MainWindow.Commands.cs` | `UI-01`, `UI-04`, `UI-05`; `AppWindowTests.Queue`, `AppWindowTests.FullScreen` | Queue, focus return, fullscreen, minimal scene IDs pending |
| Preferences, category search | Command Preferences; `MainWindow.Preferences.cs` | `UI-10`; `AppWindowTests.Preferences`, `AppWindowTests.Shell` | `preferences-normal`, `preferences-small`, three search states, 15 category captures; baseline review missing |
| Shortcut editor and menu system | Command menu, keyboard settings; `MainWindow.Shortcuts.cs`, `MainWindow.Commands.cs`, `MainWindow.xaml` | `UI-02`, `UI-11`; `AppWindowTests.Menus`, `AppWindowTests.Shell` | `menu-playback`, `context-menu`, `dialog-shortcut-sheet`; keyboard focus proof pending |
| First run / What's New / update / About | `MainWindow.Launch.cs`, `MainWindow.UpdateFlow.cs`, `MainWindow.UpdateView.cs`, `MainWindow.About.cs` | `UI-14`, `UI-15`; `AppWindowTests.About` and launch tests | `experience-welcome`; remaining overlays need fixture-backed scenes |
| Other transient tools | `MainWindow.Metadata.cs`, `MainWindow.EditDetails.cs`, `MainWindow.Transcription.cs`, `MainWindow.Effects.cs`, `MainWindow.VisualizerSettings.cs`, `MainWindow.EnhancedSubjectLock.cs`, `MainWindow.VideoStyles.cs`, `MainWindow.Faults.cs` | `UI-07`, `UI-08`, `UI-09` and respective specialist IDs; existing dialogs, transcription and fault tests | Separate capture/readiness definitions still needed; avoid overlap with #471, #472, #473 |

## Layout, transient-state and fixture observations

- The real XAML defines native `Media`, `Playback`, `Audio`, `Subtitles`, `Video`, `View` and `Help` menus. Context flyouts and stage overlays coexist with the library. Existing hotkey paths include `Ctrl+P` Preferences, `Ctrl+Shift+L` library, `Ctrl+O` open media and `Alt+Left` library Back. Do not remove their IDs or commands.
- `MainWindow.LibraryNavigation.cs` compacts below 760 logical pixels from a 200-wide rail to 64. It preserves a separate narrow-window expansion state and saves user preference only on wide windows. Visual verification is still required.
- `MainWindow.LibraryHeader.cs` expands to content height and compacts on scroll. `MainWindow.LibraryHero.cs` already uses a request generation to reject artwork from a previous page. These are existing behaviors to regression-test, not new completions.
- `LibraryScene` constructs tagged WAVs, copied video/photo fixtures, deterministic stored metadata and play counts before the app starts. This is a good starting point, but not yet a complete huge/corrupt/RTL/partial/offline matrix.
- `MainWindow.xaml` contains fixed overlay and artwork dimensions; audit resizing at 640x480, 720x640 and ultrawide before changing tokens or claiming full accessibility.
- The manifest currently has 48 capture entries; 20 use the explicitly unresolved dimension label `default-window (not yet pinned)`, and only two have a tracked CI baseline. This gap is visible instead of being represented as approved screenshot coverage.
- Current app XAML and UI tests cover menus, information panels, keyboard editor, updates, media metadata/artwork, transcription, equalizer/effects, picture styles, diagnostics and other transient views at different levels. No source existence alone counts as user-visible acceptance.

## CI image inspection and capture defect

The original #467 head's [Windows screenshot artifact](https://github.com/tochi-mba/rexplayer/actions/runs/39993169196) was downloaded and visually inspected rather than trusting file existence. `preferences-normal.png` showed the underlying idle player instead of the open Preferences dialog; `menu-playback.png` was almost uniformly white even though the visual-variation guard had let the record-only screenshot pass. These demonstrate that a successful UI Automation query or nonuniform host image alone does not establish visual correctness. This artifact is historical and is **not** the latest head.

The targeted remediation introduces `WindowPicture.TakeDesktop` only for popup scenes, an explicit `captureMode` in all 53 screenshot contracts, an expected popup-control witness, and capture JSON sidecars on failure. `Screenshots.Match` requires observable UIA readiness and visible variation inside the popup's actual screen pixels. Existing host-only captures retain `PrintWindow` to avoid a silent global capture behavior change. The Windows runner must demonstrate the new path, and a reviewer must inspect the new artifact; no pixel baseline was fabricated or promoted.

New local-only fixtures exercised by Windows UI Automation are: empty watched library, one tagged song, mixed local music/movie/photo data, and CJK/Arabic titles. They reuse `LibraryScene` and do not fetch online metadata. This is **partial** fixture coverage and not the full huge/slow/corrupt/duplicates matrix.

## Acceptance sequence

1. Fix policy failures, keep latest head CI visible and verify Windows UI independently.
2. Capture scene metadata and require manifest-listed UIA state before pixels are judged; do not bless record-only scenes.
3. Pin fixture and viewport for each priority family; create missing empty, RTL, corrupt and loading fixtures without network dependency.
4. Review Windows CI images and approve only intentional baselines; include actual/baseline/diff and semantic oracles.
5. Work in bounded shell/Home changes, then run pure, format, Windows x64, adapter, UI and perf gates on the exact final head.

No macOS capability statuses, release gates or unobserved screenshots have been promoted by this document.
