<!-- Scoped workstream source of truth. Tasks are intentionally unchecked until evidence-backed. -->

# Windows UX workstream 4/4  -  Audiobooks, podcasts, recordings and tools

## Objective and deliverables
Complete the tailored listening experience and specialist Windows surfaces: audiobook/podcast/recording pages, metadata and artwork maintenance, subtitles/transcription, equalizer, video effects, advanced Settings controls and native integration.

This is one of **four evenly partitioned draft implementation workstreams**, carved out of [original PR #467](https://github.com/tochi-mba/rexplayer/pull/467). Each owns **63 unique unchecked tasks from the original roadmap**, plus exactly eight identical release gates. Equal checkbox counts are a planning balance, not a guarantee of equal elapsed implementation time. Tasks are **not completed** merely because a checklist has been transferred.

## Scope and implementation owners
**In scope:** Audiobooks/authors, podcasts, recordings and quick sounds, metadata/artwork/downloads, caption/transcription/equalizer/tools, advanced Preferences content, onboarding, updater and diagnostics.

**Likely source/test owners:** Spoken-word library UI, metadata/artwork/update dialogs, transcription/caption tools, advanced Settings content, and related Windows tests.

**Out of scope:** unrelated audio/video decoding, analytics/network additions, breaking AutomationIDs or hotkeys, unverifiable macOS parity claims, and sister-PR tasks. Shared source changes must be coordinated before merge.

## Honest current delivery state
- [x] Preserve the original 63-task workstream and eight release gates.
- [x] Commit a first bounded local-podcast source and regression-test tranche on **this PR branch**.
- [ ] Finish the remaining podcast, transcript, recording, sound, tools and Windows UI deliverables. The first tranche does **not** complete their umbrella tasks.
- [ ] Review real Windows UI screenshots, accessibility, stress/performance results and native CI on the exact final head.

## Implementation checkpoints — 2026-10-23
**Checked checkpoints mean that the linked code/test changes were committed on PR #484, not that tests have passed or the full UX is complete.** This section supersedes inherited progress notes from other PRs.

- [x] Locate current Windows implementation and test owners: `ListeningViews`, `MainWindow.Listening`, `MainWindow.ListeningActions`, `MainWindow.LibraryPlayback`, `ListeningViewsTests` and `LibraryListeningTests`; read `AGENTS.md` and the parity requirement in ADR-0020.
- [x] Stabilize oldest/newest podcast ordering, including undated track-number ties; make show-card dates safe for empty shows, reject future-added episodes from Home and keep malformed year tags from crashing the date. Add pure tests for 1,000 episodes, empty shows, availability and future dates. [Policy](https://github.com/tochi-mba/rexplayer/commit/a9cc74e91814a9383971a5a31fc47ae3c4177b83) · [Tests](https://github.com/tochi-mba/rexplayer/commit/b58626a2dbe4eef45d4ef8eed6fb76d840a0c275)
- [x] Scope opening a podcast episode to its own show's queue rather than neighboring episodes from a mixed Home or search view. [Source](https://github.com/tochi-mba/rexplayer/commit/64c25d62676aa4ce2119588b55122866a2c3b831)
- [x] Queue only available, non-corrupt, completed local episodes for “Play unplayed”; do disk availability checks off the window thread and cancel the pending result when the route changes. [Source](https://github.com/tochi-mba/rexplayer/commit/3390491ad66c0c161b5448d05ac95b56a8a2d8a5)
- [x] Identify podcast shows as local files only; label episode file dates/year tags without asserting an unsupported published date; show known remaining time and recording source/date. [Source](https://github.com/tochi-mba/rexplayer/commit/76dc35d6f0b8220b8569d5bbe128eeab37d2c56f)
- [x] Add a Windows UI Automation scenario for a local podcast show's unplayed-order action and messaging (committed but not verified passing). [Test](https://github.com/tochi-mba/rexplayer/commit/a6be8152844b3b2a04249de81031691342fa4b87)
- [ ] Verify the pure suite and required 100% portable coverage on this branch head.
- [ ] Verify warnings-as-errors Windows build, real UI Automation, screenshot, accessibility, performance and platform/parity checks at the final head.
- [ ] Complete the 63 larger tasks and all eight release gates; no feature is declared shipped by these commits alone.

**Verification limitation:** No .NET SDK or Windows desktop runtime is installed in the editing environment, so local compilation and native UI execution were not possible. GitHub Actions was pending at the time of these edits; a pending run is not passing evidence. No generated screenshots are being passed off as real application captures.

## Detailed task list  -  63 uniquely owned roadmap tasks
The original, granular task text is reproduced so scope is not lost. No future checkbox was silently marked complete.

### P1. Audiobooks and authors
Build on existing part grouping, BookChapter, per-book speed and progress tracking.

- [ ] Book cover portrait cards, author/narrator where reliable, progress/remaining time, Continue Listening, all/completed/unstarted filters and no-cover fallback.
- [ ] Book page: title, author, cover, synopsis if available, overall progress, current part/chapter and virtualized chapter listing.
- [ ] Handle one-file M4B chapters versus multi-file book, mixed album tags, folder 'Disc 2', repeated part names, out-of-order chapters and missing lengths.
- [ ] Spoken-word transport prioritizes configurable skip backward/forward, chapter buttons, speed, sleep timer, bookmark and return to reading.
- [ ] Keep chapter progress and book progress distinct; current physical file ending must not imply entire book finished.
- [ ] Respect per-book speed across pause, resume and jumping parts; prevent speed leakage to songs/podcasts after media transitions.
- [ ] Author view: book-centered presentation without music shuffle or generic track labeling; distinguish same-title/different-author books.
- [ ] Cross-file audiobook bookmarks, if added, need stable logical book/part positions, source-file migration behavior, capability and persistence tests.
- [ ] Design book lyric/transcript coexistence carefully: no music karaoke for audiobook speech; optional textual companion only if actual text exists.

### P1. Podcasts and spoken-word transcript UX
Current local podcast classification is not proof of completed RSS subscriptions/downloads.

- [ ] Podcast Home: show-oriented square art, continue, unplayed/recent local episodes and date-aware organization.
- [ ] Podcast show detail: publisher/description/episodes, consistent sort, progress state, local-only messaging and missing-art fallback.
- [ ] Episode row: title, published date if reliable, duration, remaining time, status, short/expanded notes, Play/Resume and transcript indicator.
- [ ] Podcast player prioritize ±seconds, episode progress, speed, sleep timer, queue and chapter/note/transcript panels where available.
- [ ] Transcript view use sentences/paragraphs with timed speech highlighting, manual-scroll suspension and optional query navigation; not music-lyrics karaoke layout.
- [ ] Show notes must handle long markup safely: no HTML/script execution, safe links, truncation and keyboard-readable text.
- [ ] Queue 'play unplayed in order' uses actual availability; never autoplay a different show's episodes unexpectedly.
- [ ] Keep RSS feed subscription, built-in downloads and podcast network features gated by existing `PLF-08`, `NET-15` and relevant `NET-*`; do not draw live Subscribe/Download controls prematurely.
- [ ] Test local shows with 1/1000 episodes, no dates/art, identical episode names, incomplete transcripts, offline and stopped downloads.

### P1. Recordings and short sounds
Existing record-library browsing is separate from future capture/record functionality.

- [ ] Recordings: date sections (Today/Yesterday/This Week/older/undated), voice-note group, compact rows, source/date/duration and correct local-file actions.
- [ ] Waveform detail when added: analysis off UI thread, bounded cache, seek/zoom/pan/time ruler/current playhead and cancellation on media switch.
- [ ] Recording details: filename/title, date, duration, path, optional waveform, transcript, bookmarks and clear remove-from-library versus delete-file semantics.
- [ ] Transcript mode: search/jump to timed speech if indexed, manual-scroll suspension and clear partial/edited/transcription-error status.
- [ ] Review mode: frame-irrelevant audio tools, exact seek and playback speed; use existing `PB-08` for future A-B loop rather than inventing duplicate capability.
- [ ] Short sound mode: compact title/duration, replay, repeat and volume; no enormous book cover or unnecessary full animation.
- [ ] Optional true capture uses existing `AU-24`/`OUT-06`/`CAP-*` rows and separate permission/source/record/pause/stop/format/storage/error workflows; do not conflate with playing recordings.
- [ ] Test 0-second, hours-long, silence, clipped loudness, mono/stereo/multichannel, overlapping jobs, moved files, missing timestamps and enormous transcript.

### P1. Metadata, artwork, downloads and maintenance
Improve visible existing behavior before inventing new library metadata APIs.

- [ ] Media-specific facts editor with provenance (embedded file, folder art, local override, optional online enrichment, generated fallback) only when source data is available.
- [ ] Song/multi-song Edit Details: mixed-value fields, safe no-op untouched fields, validation, deferred writes for playing files and progress/error feedback.
- [ ] Artwork choose/preview/override: aspect, resolution, cancel/reset, no source mutation unless user explicitly chooses writing.
- [ ] Indexing and refresh: non-blocking status, meaningful errors, no visible route reset and stable selected item during metadata completion.
- [ ] Existing file-growing/download badges: correct partial state, no fabricated percentage, unfinished-file play feedback and clear completion handling.
- [ ] Library backup/restore: scope, destination, confirmation, transaction/error state and never silent destructive replacement.
- [ ] Missing-file recovery: disconnected drives, moved/renamed paths, inaccessible UNC/permissions, stale playlist references and recovery without deleting history.
- [ ] Potential duplicate detection is gated by existing `LIB-12`; do not mark complete through UI alone.

### P1. Playback timelines, subtitles, transcription and specialist tools
Improve player overlays and advanced workspaces while preserving engine and current subtitle/transcription semantics.

- [ ] Common transport: readable elapsed/remaining times, precise thumb hit target, seek hover/time tooltip, buffered/transcript rails, markers and correct keyboard controls.
- [ ] Full-screen: reveal/hide policy, mouse/keyboard/touch interaction, taskbar/z-order, multi-monitor, no flashes and safe subtitle area.
- [ ] Existing intro/credits/bookmark markers should not collide with timeline tooltips, captions or volume flyouts; preserve marker precision and hit targets.
- [ ] Keep separate subtitle/secondary subtitle tracks, language selection, delay/sync and styling; only show controls supported by loaded media.
- [ ] Subtitle appearance preview: realistic bright/dark media sample, font/outline/placement/dual-track layout and high-contrast alternative.
- [ ] Kinetic captions: preserve current word choreography, optional normal subtitle coexistence, local transcription/cue source, picture-avoidance and resource cancellation.
- [ ] Transcript editing: original/edited distinguishable, timing fidelity, partial coverage, save/cancel, export, model selection, searchable text when actually implemented.
- [ ] Transcription queue: queued/running/paused/completed/error jobs, model download state, progress, cancel, disk-space issue, no inappropriate modal blocking.
- [ ] Equalizer: 10-band labels, preamp/zero line, saved preset, live preview, keyboard controls and current audio processing preserved.
- [ ] Picture effects and Subject Lock: clear state, accurate non-destructive preview language, status/errors, keyboard focus, small viewport, panel location away from captions.
- [ ] Playback technical/statistics/log/info/diagnostics: compact, readable, copyable, error detail and privacy-aware export.
- [ ] Future `PB-07/08/09/26`, `VID-28`, `NET-*`, `CAP-*`, `OUT-*` interfaces must not appear working until dependencies have been implemented and separately verified.

### P1. Preferences content, onboarding, updates and Windows integration
PR #467 already owns Settings category navigation and search; this work refines each control and the remaining application windows.

- [ ] Audit Playback, intros/credits, Video/Pictures, Music/Artwork, Performance, Languages, Subtitles, Transcription, Window, Library, Metadata, History/Resume, Keyboard/Mouse, File Opening and Updates section contents.
- [ ] Keep Save/Cancel, section-scroll retention, responsive rail/picker and existing AutomationIDs; improve control descriptions, units, grouping, focus and validation.
- [ ] Live subtitle/picture preview and advanced grouping are outstanding original tasks; implement in a later bounded PR rather than claim complete now.
- [ ] Hotkey editor: recording, conflict resolution, app-global distinctions, search, reset, keyboard focus and mixed input methods.
- [ ] First-run: Open File/Add Folders routes, opt-in explanation for network/metadata/models and no account requirement.
- [ ] What's New/About: version, release details, diagnostics, copy links, accessible long text and visually coherent branding.
- [ ] Updater: indicator, available/downloading/paused/retry/error/restart states, no playback interruption, honest progress and offline/metred-network behavior.
- [ ] Unify dialogs/flyouts/in-window confirmations and validation error presentation; cancel/Escape/focus restoration/destructive choice consistency.
- [ ] Respect native title bar, Snap, file associations, taskbar/SMTC/Jump List, high DPI, Windows 11 theme materials and minimal-window mode without regressions.

### P0. Edge-case matrix and quality gates
Each item must be a test or an explicit reviewed exclusion, never silently assumed safe.

- [ ] Metadata: invalid/unicode names, duplicate titles, long multiline titles, ambiguous artists, incorrect seasons, no source art, malformed tags, very long synopsis, incomplete optional provider response.
- [ ] Windows: topmost/normal overlap, minimized main window, detached companion focus, fullscreen video while lyric window topmost, monitor removal, DPI crossing, suspension/resume and app shutdown.
- [ ] Storage: disconnected external drive, network path not ready, directory permission denial, file renamed while open, partial download, duplicate path, long Unicode path, save canceled and failed metadata write.

### P0. Architecture, tests and per-PR execution contract
Each future agent should take one unchecked workstream, select bounded tasks and deliver one independently reviewable follow-up PR.

- [ ] Release note: new UI must not be called shipped until tests, baselines and manual Windows acceptance are verified on the actual final branch head.

### Required screenshot families (expand into stable fixture-backed IDs)
- [ ] Books: Home, continue, cover, book detail, author, chapters, single- versus multi-file, speed saved, timer, finished, unknown narrator
- [ ] Podcasts: shows, many episodes, notes, dates, unplayed filter, transcript, no transcript, offline, chapter panel
- [ ] Recordings/sounds: date groups, voice notes, waveform if implemented, transcript, bookmarks, review and compact quick audio
- [ ] Tools: subtitle styles, dual captions, kinetic captions, transcript editor/partial progress, transcription jobs, equalizer, picture effects, Subject Lock, stats and info

## Standard completion gates  -  8 checks per PR
- [x] Map current routes, code owners, capability IDs and existing UI assertions before changing behavior (see implementation checkpoints; existing assertions are not claimed as freshly passing).
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
- 3/4  -  Music, lyrics and visualizers
- **4/4  -  Spoken word, metadata and tools (this PR)**

**Merge gate:** Remain draft until actual implementation, full task evidence, reviewed real screenshots and green current-head CI exist. A planning document is not a completed feature. Original complete source and roadmap remain traceable in [#467](https://github.com/tochi-mba/rexplayer/pull/467).
