<!-- Scoped workstream source of truth. Tasks are intentionally unchecked until evidence-backed. -->

# Windows UX workstream 3/4  -  Music, lyrics and visualizer experience

## Objective and deliverables
Improve Music Home, songs/albums/artists, line- and word-timed lyrics presentation, detached lyrics companion and existing visualizer catalog, retaining performance controls and camera permissions.

This is one of **four evenly partitioned draft implementation workstreams**, carved out of [original PR #467](https://github.com/tochi-mba/rexplayer/pull/467). Each owns **63 unique unchecked tasks from the original roadmap**, plus exactly eight identical release gates. Equal checkbox counts are a planning balance, not a guarantee of equal elapsed implementation time. Tasks are **not completed** merely because a checklist has been transferred.

## Scope and implementation owners
**In scope:** Music browsing, now-playing stage, local lyrics parsing and display, optional companion window, visualizer menus, existing choices, consent and lifecycle/performance.

**Likely source/test owners:** Music library views, `MainWindow.Presentation.cs`, `MainWindow.Visualizer.cs`, `AppCore/Player/Lyrics.cs`, visualizer models and Windows tests.

**Out of scope:** unrelated audio/video decoding, analytics/network additions, breaking AutomationIDs or hotkeys, unverifiable macOS parity claims, and sister-PR tasks. Shared source changes must be coordinated before merge.

## Follow-up PR #488: evidence-based work completed

The [library and shell evidence map](implemented-library-shell-evidence.md) links the new persisted real-album Music Home pins, Unicode and malformed genre handling, exact word-timed lyrics rendering, suspend/resume lyric follow, per-mode visualizer setting isolation, native automation labels, and their focused tests. It does **not** claim a detached lyrics companion, finalized Mac equivalence, all physical-device transitions, or human-reviewed screenshots. The [recovery ledger](merged-pr-followup-audit.md) retains the exact remaining acceptance gates.

## Follow-up #487.6: native lyric-mode capture code authored (2026-10-24)

The five mutually exclusive `LyricsDisplayMode` values have **real WinUI fixture coverage** in `AppWindowTests.LyricsScreenshots.ActualLyricsModesHaveNativeStageCaptures`: missing source (None), plain, line-timed, mixed/partly word-timed, and fully word-timed. The test creates a local 40-second WAV and factual per-test LRC data, waits for the exact `LyricsStatus` explanation, pauses playback, checks native `Lyrics` visibility and the presence or absence of correctly named seek buttons, and records `Stage` captures at **900×700 and 720×640**. `TimedLyricActionsSeekFromAccessibleNativeButtonsAndKeepTheModeHonest` also drives a real timed button through UI Automation and checks that the player seeks to the source's 20-second timestamp.

`MainWindow.Presentation` now uses Windows high-contrast foreground/background for lyric text, timed words, and their small backing chips instead of overriding the system palette with hard-coded translucent white/dark values. It responds to XAML theme and OS high-contrast changes without stealing focus or restarting manually suspended lyric following.

This closes the **screenshot automation/capture authoring** requested by #487.6; it does *not* claim a completed Windows run, reviewed screenshot baselines, high-contrast native pixel checks, screen-reader certification, or a release-ready lyric image. #487.7 and the relevant #486 native verification gates remain open. The screenshot harness records missing baselines but cannot approve them.

## Honest current delivery state
- [x] Transfer these requirements to an independently owned, versioned scoped plan in this branch.
- [ ] **Complete the actual code and tests.** The PR started as a planning-only draft; a first lyrics-model implementation and regression tests now exist, but the full scope and required verification remain unfinished.
- [ ] Attach actual Windows captures and native CI results before claiming feature readiness.

## Implementation progress journal (2026-10-23)
- [x] Fix the pre-existing portable CI failure caused by prohibited punctuation in this plan.
- [ ] Verify enhanced LRC word segments and logarithmic line seeking on exact-head CI (`3c554ab`, `23b82e9`, `517218c`); parser and regression tests committed.
- [ ] Verify the exhaustive visualizer-option round-trip test (`89d53f5`); all modes and their per-mode persisted settings remain in scope.
- [ ] Build and verify remaining styles, companion, music pages, visualizer gallery, platform UI automation, screenshots and performance evidence. No broader task is complete yet.

## Detailed task list  -  63 uniquely owned roadmap tasks
The original, granular task text is reproduced so scope is not lost. No future checkbox was silently marked complete.

### P0. Music Home, songs, albums, artists and genres
Music already has a specialized Home, artist/album compositions, Songs table, artwork tint and quick actions.

- [ ] Refine music Home composition: album/square cover, distinct round/portrait artist treatment, listening shelves, mix/playlist cards, duplication control and quick Play.
- [ ] Songs table: sortable stable columns, playing/focus/selection treatment, artist/album links, duration/play-count numeric alignment, compact/narrow handling.
- [ ] Album page: cover scale, year/track count/duration, multi-disc grouping, main and featured artists, play/shuffle/queue and per-track actions.
- [ ] Handle compilations, missing track/disc indices, alternative album versions, different contributing artists, extremely long classical music metadata and unknown years.
- [ ] Artist page: portrait tint, readable summary, top songs, albums, Featured On, no-photo fallbacks and correct keyboard/back navigation.
- [ ] Genres: useful albums/artists grouping without one inappropriate giant media-card template; handle multiple/inconsistent tags and unknown genres.
- [ ] Music now-playing: artwork-first, expanded player, current song/artist/album and queue/lyrics/visualizer contextual areas with stable underlying player.
- [ ] Lyrics: preserve current timed LRC, plain lyrics, click-to-seek, current-line highlight and lyric on/off; overhaul layouts separately below.
- [ ] Use image dimension-aware caching and no-cover fallbacks; no stretched portrait/album cover or stale tint after rapid track switching.
- [ ] Cover one-track releases, live sets, cue sheets, lossless labels only if reliable, missing audio art, gapless transitions and album replay.

### P0. Lyrics overhaul and screenshot-inspired detached companion
**Current baseline:** `MainWindow.Presentation.cs` uses timed/untimed `Lyrics`, centered line buttons, current-line highlight and bring-into-view within `LyricsBox` (maximum 720 width, ~45% stage height, fixed 20-point text). Preserve every existing behavior.

- [ ] Create a reusable lyrics document/presentation contract that separates parsing/timing from XAML layout; do not duplicate lyric data or playback clocks.
- [ ] Implement style 1: detachable, movable, resizable lyrics companion with artwork/title/artist header, previous/current/next emphasis, timeline and optional compact transport.
- [ ] Implement style 2: balanced artwork + lyrics split mode with artwork-derived but contrast-safe background; allow column ratio/reversed orientation and responsive stack.
- [ ] Implement style 3: docked lyrics sidebar while library remains interactive; preserve route, scroll, focus and coexistence rules with queue/context panels.
- [ ] Implement style 4: document-like reading sheet for *untimed* lyrics with verse breaks, selectable text, manual scrolling and comfortable typography.
- [ ] Design style 5: immersive lyrics theater for fullscreen/windowed use, huge readable lines, artwork or solid backdrop and safe transport reveal.
- [ ] Refine style 6: existing minimal overlay compatible with visualizers without broad opaque blocks; protect current song/transport and automatic line visibility.
- [ ] Gate style 7: actual word-timed/karaoke highlighting behind reliable timing data; preserve line-only LRC as line-only, never distribute syllable timing as though precise.
- [ ] Provide layout chooser (explicit text names, thumbnails, keyboard selection) and a live style editor: size/weight/alignment/spacing/line context/contrast/active emphasis/artwork/blur/animation/auto-follow.
- [ ] Define high-readability, restrained, immersive and reading presets and per-style persistence; keep lyric theme separate from media layout and subtitle styling.
- [ ] Timed line clicks should seek exactly; keyboard Enter/Space should work; selecting text must not inadvertently seek; untimed lines must not imply timestamps.
- [ ] Auto-follow should suspend on user wheel/drag/keyboard scroll and expose an unobtrusive Return to Current control; never snap back immediately on the next position tick.
- [ ] Use authoritative playback position for current line: rapid seek/rewind, pause/resume, speed changes, gapless transitions, time offsets, repeated/empty lines, negative LRC starts and song end.
- [ ] Support multiline wrapping, long verses, CJK, mixed direction, RTL, duplicate timestamps, nonmonotonic/invalid timestamps and sufficiently efficient rendering of very long lyrics.
- [ ] Detached window: one shared playback session, no new audio sink, independent resize/move, optional topmost, close ≠ stop, minimize behavior, offscreen recovery and mixed-DPI dual monitors.
- [ ] Detached window should not steal focus on every lyric update, trap fullscreen video, float unrequested over unrelated media or persist stale lyrics when a song changes.
- [ ] Missing lyrics/source revoked: preserve window state but show artwork, song identity and clear unavailable/import action, never previous track's lyrics.
- [ ] Consider per-track non-destructive lyric timing offsets only with distinct documented persistence and test coverage; do not rewrite source LRC by default.
- [ ] Word-level LRC requires AppCore model extension: `Lyrics.Parse` currently removes `<mm:ss.xxx>` word markers; preserve usable segments and validate ranges before enabling karaoke.
- [ ] Add independent lyric/layout/companion UIA and visual tests for styles, themes, timing, pause/seek, high DPI, RTL, large text, no art, no lyrics, track changes and screen-reader descriptions.

### P0. Audio visualizer compatibility  -  protected existing architecture
**Critical:** `MainWindow.Visualizer.cs` already supports dozens of audio-reactive modes, a separate `AudiobookVisualizer`, `VisualizerGroups`, option palettes, on-demand camera effects, software stage, `MusicPulse` analysis, `_visualPacing`/`_budget`, renderer sizing and cleanup. This overhaul must use - not circumvent - that system.

- [ ] Inventory every `VisualizerChoice` and `VisualizerGroups` family (spectrum, waveform, meters, spectrogram, light/scene simulations and all camera-driven variants); none may disappear from selection or saved settings.
- [ ] Preserve `VisualizerOptions` keys, palette choices, sensitivity, meter, camera/cover source options and per-mode defaults/range normalization; old values must round-trip through new UI.
- [ ] Preserve `ActiveVisualizer` selection semantics: separate audiobook setting; other non-video audio uses general fallback unless deliberate tested policy extension is added.
- [ ] Ensure media-adaptive layout resolution does not silently force a visualizer off, switch audiobook's preferred visualizer to music's, overwrite visualizer preference or forget the selected renderer.
- [ ] Build a gallery/picker with authentic static preview images or sandboxed deterministic samples, group and label correctly, show Off/active/camera status and maintain existing menu/shortcut selection.
- [ ] Visualizer Stage player mode: choose whether visualization is full bleed, next to cover, behind lyric text, or hidden under an independent lyric panel; maintain explicit z-order and readable overlay contrast.
- [ ] Music + visualizer + overlay lyrics must work with current lyric stage; split/docked/floating lyrics may render separately without disabling the selected visualizer unless the user deliberately chooses a no-visualization layout.
- [ ] Do not create a second MusicPulse analyzer, second camera feed, second writeable bitmap per companion pane or duplicate composition rendering simply to support layout previews.
- [ ] Preserve `Visualizer.SizeChanged`/`BuildVisualizer`, stage pixel scaling (`XamlRoot.RasterizationScale`), `_budget.VisualMaxSide`, perf-tier quality and `VisualPacing` frame skipping on every new host size.
- [ ] On resize, detach/dock, 4K/ultrawide/high DPI and fullscreen switch, resize canvases and stage at controlled frequency, avoid flicker/OOM, reject stale async results and never scale cached images incorrectly.
- [ ] Preserve drawing only for valid audio sessions and stop drawing when inactive; maintain `StopVisualizing`, `StopCamera`, memory-release and camera restart behavior across all layout and media transitions.
- [ ] Guard camera-specific modes behind existing permission prompt, no implicit opt-in, no recording/uploading and release camera whenever no camera-dependent visualization remains visible.
- [ ] Handle missing/blocked/unplugged camera, privacy toggle, camera busy, repeated failure/retry, suspend/resume, app close and switching from a camera mode to ordinary audio.
- [ ] Prevent secondary/hidden visualizer hosts from continuing expensive rendering just because a lyric companion is open; run exactly the work that is visible and necessary.
- [ ] Keep camera-active explanation distinct from microphone/audio input; never pretend existing camera visualization permission grants capture-to-file permission.
- [ ] Test off/silence, low volume/mute, paused, seek, crossfade when implemented, gapless transitions, channel changes, varying sample rates, output-device changes and file errors.
- [ ] Test every family at 100/150/200% DPI, small/large viewports, battery saver/low-end software render, lyric panel open/closed and high-contrast/reduced-motion settings.
- [ ] Record per-mode draw cost using existing `ExperiencePerfTests` (`visual-1080p-ms-*`) and CI perf lane; no artificial new CI baseline from local machines.
- [ ] Respect photosensitivity and reduced-motion guidance: strobe/flash warnings, optional static/slow alternatives, no unexpected camera or aggressive animation on startup.
- [ ] Guarantee independent lyric styling does not modify visualizer parameters, palettes or sample analysis; preview Save/Cancel restores the exact previous selection and options.
- [ ] Screenshot gallery, menu, all visualizer groups, representative individual modes, camera permission/declined/unavailable, visualizer+lyrics split/overlay, album art absent and low-end fallback.

### P0. Edge-case matrix and quality gates
Each item must be a test or an explicit reviewed exclusion, never silently assumed safe.

- [ ] Offline/privacy: zero network, no optional metadata, no speech model, declined camera, disabled transcription, hidden history and no telemetric side effects.
- [ ] Visualizer: every choice and option group, low-end software rendering, strobe photosensitivity, camera busy/revoked, silence/paused/muted, visualizer off, audiobook separate option and lyrics coexistence.
- [ ] Text: untimed LRC, line-timed/word-timed lyrics, duplicate/invalid time tags, empty lines, long verses, mixed RTL/CJK, generated speech timing gaps and no captions.

### P0. Architecture, tests and per-PR execution contract
Each future agent should take one unchecked workstream, select bounded tasks and deliver one independently reviewable follow-up PR.

- [ ] Preserve `REXPLAYER_UI_LANE=ci-windows`, screenshot diff artifact policy, `REXPLAYER_FAKE_AUDIO=1` and model-free deterministic fake speech when needed.
- [ ] Never update `tests/perf-baselines.json` from a local machine; only use an evidence-backed CI lane measurement as documented.

### P0. Capability matrix, generated parity and documentation protocol
Source of truth is `docs/capability-matrix.json`. Generated files are never hand edited. The Windows-only UI project does **not** grant permission to claim cross-platform completion.

- [ ] Additional **candidate** new IDs: `LIB-13` pinned/saved library layout; `LIB-14` manual watched-state correction; `LIB-15` logical cross-file book bookmarks; `LIB-16` smart collections; `AU-25` waveform analysis/navigation; `SUB-22` timestamped transcript search.
- [ ] Additional **candidate** new IDs: `META-10` provenance inspector; `META-11` word-timed lyrics; `META-12` non-destructive lyric offset. Do not allocate duplicates if detailed review places the behavior inside an existing ID.
- [ ] Document layout preference schema/versioning, migration from `ShowLyrics`, visualizer choice persistence, standalone lyric-window geometry, user privacy, resource lifecycle and fallback routes.
- [ ] Never claim the proposed IDs or planned UI as implemented merely because they are enumerated in this PR roadmap; only check a task after its follow-up PR is merged and verified.

### Required screenshot families (expand into stable fixture-backed IDs)
- [ ] Music: Home/album/artist/genre, song dense rows, multi-disc and compilations, no-cover, selected/playing, long title, now-playing, lyrics toggled
- [ ] Lyrics: original baseline, detached light/dark/compact/multimonitor, split/reversed, docked, theater, overlay, reading, karaoke if timed, editor, no lyrics, RTL, CJK, line seek
- [ ] Visualizer: picker grouped by mode, spectrum/oscilloscope/meters/spectrogram/representative scene and camera mode, off, no permission, unavailable device, lyrics over visualization, 4K/low-end

## Standard completion gates  -  8 checks per PR
- [ ] Map current routes, code owners, capability IDs and previously passing UI assertions before changing behavior.
- [ ] Implement each owned feature in separately reviewable source changes without altering the core decoder/engine contract.
- [ ] Add deterministic offline fixtures for normal, empty, long/RTL, missing, corrupt, slow and partial data relevant to this scope.
- [ ] Prove interactions, persistence, cancellation, navigation restoration and errors with executable functional/pure tests.
- [ ] Add real Windows UI Automation checks for semantic states, labels, actions, scroll and focus rather than relying solely on pixels.
- [ ] Capture actual Windows light/dark/compact/wide layouts and applicable high-contrast, text-scaling and keyboard scenarios against reviewed CI baselines.
- [ ] Measure relevant render/scroll/resize/playback/artwork costs and fix unexplained regressions without relaxing baseline limits.
- [ ] On the final head, verify build, format, tests, native UI and review artifacts, then update accurate CHANGELOG/capability/parity entries.

## Verification and acceptance artifacts
| Evidence | Required proof |
| --- | --- |
| Functional | User-visible states and actions verified with UI Automation; cancellation, focus, selection and navigation have assertions. |
| Visual | Actual `rexplayer.exe` captures, never generated mocks; compact + normal + relevant high-DPI/high-contrast cases with reviewed baseline diff. |
| Fixtures | Deterministic offline media and metadata; missing, malformed, unicode and partial-data cases for owned controls. |
| Performance | No unexplained latency/scroll/resize/memory regressions, stale image/lyric callbacks or flickering UI. |
| Build & CI | Windows warnings-as-errors build, pure/unit/UI tests and screenshot artifacts green on the latest exact head. |
| Documentation | Honest CHANGELOG, accurate existing/new capability IDs and generated parity docs; no unverified macOS claims. |

## Coordination and order
The four parts are independent draft PRs against `main`; none may assume unchecked changes elsewhere have shipped. Foundation (1) owns the existing Settings search, screenshot guard and shared shell. Specialist Settings content (4) must reuse it after landing and **not duplicate the foundation**. Video/player (2), music/lyrics/visualizers (3), and spoken-word/tools (4) must agree on existing playback-stage and focus contracts before overlapping edits. Follow `AGENTS.md`, the clean-room rules and ADR-0020; preserve automation and media engine invariants.

## Sister workstreams
- 1/4  -  UI foundation and deterministic QA
- 2/4  -  Video and adaptive playback
- **3/4  -  Music, lyrics and visualizers (this PR)**
- 4/4  -  Spoken word, metadata and tools

**Merge gate:** Remain draft until actual implementation, full task evidence, reviewed real screenshots and green current-head CI exist. A planning document is not a completed feature. Original complete source and roadmap remain traceable in [#467](https://github.com/tochi-mba/rexplayer/pull/467).
