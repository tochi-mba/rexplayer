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
- [x] Transfer these requirements to an independently owned, versioned scoped plan in this branch.
- [ ] **Implement the actual code and tests.** The PR began as a *planning-only draft*. Bounded source fixes and regression tests are now present, but the full owned scope and verification remain open.
- [ ] Attach actual Windows captures and native CI results before claiming feature readiness.

## Implementation checkpoints  -  2026-10-23
These checkpoints track **committed changes**, not feature acceptance or passing test results. They do not close any of the 63 roadmap tasks or eight release gates.

- [x] Read `AGENTS.md`, ADR-0020 and existing LIB-05 coverage. Mapped current spoken-word logic to `ListeningViews`, `MainWindow.Listening`, `MainWindow.ListeningActions` and `MainWindow.LibraryPlayback`; mapped existing portable and Windows UI assertions to `ListeningViewsTests` and `LibraryListeningTests`.
- [x] Commit guarded book resume/progress behavior for empty books, missing playback timestamps, missing/zero durations, and out-of-range positions; add portable regression assertions. [Source](https://github.com/tochi-mba/rexplayer/commit/682a1c4064e255ce6f1332f38503aed50cf0cc15) · [Tests](https://github.com/tochi-mba/rexplayer/commit/1450c003bc7c8ccb910f8edf3611088009af2dce)
- [x] Commit Authors → Book → Chapter queue isolation and its Windows UI Automation regression test. [Source](https://github.com/tochi-mba/rexplayer/commit/21a4f6d736cdc5b5faaf6cce3c19113a95d30228) · [Test](https://github.com/tochi-mba/rexplayer/commit/8f2edca277aae774207dee7663c44b7bccf8c529)
- [x] Commit chronological chapter ordering and one-pass active-chapter selection for duplicate/out-of-order markers; update both book routes and add pure regression tests. [Logic](https://github.com/tochi-mba/rexplayer/commit/6d108bc22a540320dc7056eac723aefb40d209aa) · [UI](https://github.com/tochi-mba/rexplayer/commit/e7183458e0f2a26b273b8cb1604dff78ead01103) · [Tests](https://github.com/tochi-mba/rexplayer/commit/b9fe534e407c1718f9e9dc000e0b7770bd81c390)
- [ ] Confirm pure and coverage suites are green on the final branch head; tests were committed but have not yet been observed passing.
- [ ] Confirm Windows UI Automation, screenshot, accessibility, performance and warnings-as-errors evidence on the final branch head.
- [ ] Continue with the remaining roadmap tasks; **none of the 63 umbrella tasks is represented as fully delivered by these fixes**.

**Environment note:** No local .NET SDK or Windows test runtime was available for this edit session. GitHub Actions is the source for actual build/test verification; its results must not be inferred while a run is pending.

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
- 3/4  -  Music, lyrics and visualizers
- **4/4  -  Spoken word, metadata and tools (this PR)**

**Merge gate:** Remain draft until actual implementation, full task evidence, reviewed real screenshots and green current-head CI exist. A planning document is not a completed feature. Original complete source and roadmap remain traceable in [#467](https://github.com/tochi-mba/rexplayer/pull/467).
