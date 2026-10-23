<!-- Scoped workstream source of truth. Tasks are intentionally unchecked until evidence-backed. -->

# Spoken-word roadmap trace — podcasts, transcripts, recordings and sounds (PR #484)

## Objective and deliverables
Deliver the Windows local podcast, speech-transcript, recording and short-sound experiences described in [PR #484](https://github.com/tochi-mba/rexplayer/pull/484), successor **2 of 3** of [#473](https://github.com/tochi-mba/rexplayer/pull/473). Never portray RSS subscriptions, downloads, recording capture or network enrichment as available until their capabilities are implemented.

This file carries the **historical 63-task workstream 4/4 trace** inherited from [#467](https://github.com/tochi-mba/rexplayer/pull/467). It is retained for requirement discovery, **not** a claim that this successor owns all 63 tasks or has completed them. Audiobooks and the metadata/tools tasks are delegated to the other #473 successors; original checkboxes stay unchecked here for traceability.

## Scope and implementation owners
**Owned by PR #484:** local podcast Home and show/episode presentation, spoken transcripts with honest availability, safe notes if stored, show-isolated queue behavior, recordings grouping/actions, optional audio waveform and review behavior, compact short-sound playback, and scoped fixtures/tests.

**Likely source/test owners:** `ListeningViews`, `MainWindow.Listening`, `MainWindow.ListeningActions`, `MainWindow.LibraryPlayback`, relevant transcript surfaces, `ListeningViewsTests`, `LibraryListeningTests`.

**Out of scope:** audiobook implementation, metadata/artwork/tools and advanced Settings tasks assigned to the other #473 successors; unimplemented RSS/download/capture capabilities; unrelated decoders or engine contracts; breaking existing AutomationIDs; unverified macOS parity. Coordinate any overlap before merging.

## Honest current delivery state
- [x] Preserve the original 63-task historical trace and eight release gates without misrepresenting successor ownership.
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
- [x] Add a pure, line-coverage-gated transcript word/phrase finder, including Unicode and 100,000-word tests; wire Find next and seek to the existing paged transcript editor and test it with Windows UI Automation. [Search](https://github.com/tochi-mba/rexplayer/commit/41423a259e33664691a8c873d2ba6ce08a402c15) · [Pure tests](https://github.com/tochi-mba/rexplayer/commit/12f50c0424dc61014bd8205de028c6ffc327c593) · [Editor](https://github.com/tochi-mba/rexplayer/commit/f498037d29424d0c0a608a1287d02b4981658ef5) · [UI test](https://github.com/tochi-mba/rexplayer/commit/6efbd5850c2689235b91f091138c3820518526e4)
- [x] Prevent recording or sound activation from queuing adjacent recordings; add a compact Replay sound command and Windows UI automation assertion. [Playback](https://github.com/tochi-mba/rexplayer/commit/f55cdc01eca292366ef2054d34c75e91a25e64ab) · [Replay](https://github.com/tochi-mba/rexplayer/commit/79571b87bf48add9e94a4112fb19a8838bcf2712) · [Test](https://github.com/tochi-mba/rexplayer/commit/b33076c3250f8c7a72df86de2271b2e2b554b64c)
- [x] Commit the paginated local speech transcript reader (bounded paragraphs, manual follow suspension, keyboard-readable timed jumps and search) and a Windows UI Automation scenario. [Reader](https://github.com/tochi-mba/rexplayer/commit/0df0de69c504256e2bf0b704d49d12d3726992ba) · [Menu](https://github.com/tochi-mba/rexplayer/commit/1b5be67f14163694c0c701c30916b129b1bca087) · [Test](https://github.com/tochi-mba/rexplayer/commit/360ef3b4770ba3dde6d432e711b69575f8dd0848)
- [x] Commit the asynchronous cancellable sampled recording waveform (four-file bounded cache, zoomable horizontally panned bins, playhead and timed seek), pure zero-length/clipped/long-file tests and Windows UI entry-point assertion. [Policy](https://github.com/tochi-mba/rexplayer/commit/b55f852784c9552fbbe6367c348eed22d36ea246) · [Windows](https://github.com/tochi-mba/rexplayer/commit/41734da287ab654dbe15ec0cfe4a55b2805c46da) · [Test](https://github.com/tochi-mba/rexplayer/commit/ac2a85ed13506c604d0469d9ee77f0754356608c)
- [x] Commit the compact short-sound replay/repeat/volume/duration panel and Windows UI regression; scope repeat to dialog lifetime. [Controls](https://github.com/tochi-mba/rexplayer/commit/b40a2ac3f75699bceeef45b9a196a921cadb506a) · [Test](https://github.com/tochi-mba/rexplayer/commit/bad786202051dfcec6c9886279c2ceb5799337b8)
- [x] Distinguish file-modified timestamps from reliable filename timestamps on recording rows, including invalid/undated cases. [Policy](https://github.com/tochi-mba/rexplayer/commit/b6aa5c91b45262de38f606617baa4a798a27fe2d) · [UI](https://github.com/tochi-mba/rexplayer/commit/7a0938df828d7c85f549fd2d22d3b2e3129a97c1) · [Tests](https://github.com/tochi-mba/rexplayer/commit/92b4c99e2afdfe5cea3aca53705844cb8a7d6313)
- [x] Read cached transcripts from podcast and recording rows without automatically playing a file; timed paragraph selection starts only the selected local item. [Reader](https://github.com/tochi-mba/rexplayer/commit/6c27b84d69538e367bedbf12e4e88d25a76afcbf) · [Menu](https://github.com/tochi-mba/rexplayer/commit/fe33492be98f3a9e3975aea371312dde429590b2) · [Test](https://github.com/tochi-mba/rexplayer/commit/cef2684f71ac6a0b33b153f23be34d12b3c09d94)
- [x] Show explicit local plain-text episode notes with a bounded read, literal/inert markup, no unsafe auto-open links, long-text and RTL/Unicode coverage plus Windows UI test. [Policy](https://github.com/tochi-mba/rexplayer/commit/448ea11cab2e1f1944c84cd24b2ac7320aba4cc9) · [UI](https://github.com/tochi-mba/rexplayer/commit/5f6e92e9d9b99e6a7c459e7b731edd431227f4b1) · [Tests](https://github.com/tochi-mba/rexplayer/commit/5389c9ba1b196c5ec92f03e1898ea94489b827c0)
- [x] Add dedicated recording details with date evidence, duration, selectable location, exact seek, bookmarks and clear non-destructive remove-versus-delete messaging; UI assertion for separate menu actions. [UI](https://github.com/tochi-mba/rexplayer/commit/19f22c0722713c8f6878351b4cfc9e1658165a65) · [Test](https://github.com/tochi-mba/rexplayer/commit/1365e6fc4593bf7770e96cd46f8ee4807892aab8)
- [x] Add local podcast spoken transport (play/resume, 15s back, 30s forward, speed, sleep and indexed chapter actions) with UI automation checks. [UI](https://github.com/tochi-mba/rexplayer/commit/5548b6069432cc22f4541abb259028dfb0cf9807) · [Test](https://github.com/tochi-mba/rexplayer/commit/20b69593057fe8c85317e294fced5267d3d0cf8f)
- [x] Add local podcast show cards to Home (unheard-first, recent local evidence, safe art fallback), isolated click navigation, unique AutomationIDs and a Windows UI scenario. [Home](https://github.com/tochi-mba/rexplayer/commit/bb7b8ba2023d305569452d477167395988fb88e6) · [No mixed queues](https://github.com/tochi-mba/rexplayer/commit/50b4c823ac8f40a6258e37ff5a09974ecd854b99) · [Test](https://github.com/tochi-mba/rexplayer/commit/d74dc02f50751c4928537d09691792602e6b1bbc)
- [x] Show optional safe local show description on the podcast detail page (async read, 64-entry bounded cache, refuses shared/multiple-folder attribution), explicitly labeling creator-tag evidence rather than a fabricated publisher. [Policy](https://github.com/tochi-mba/rexplayer/commit/f41270143cfb3f91140fc09338315b999cd9128e) · [UI](https://github.com/tochi-mba/rexplayer/commit/d75cc116c8fcdb7879c64a5b7086f57e47a80946) · [Test](https://github.com/tochi-mba/rexplayer/commit/51cbbc788028cc81408259e998968c656e06c9fe)
- [x] Move episode state/progress into pure, required-coverage policy with offline/missing-duration/out-of-range/damaged/partial tests, and add undated duplicate-title grouping assertions. [Progress](https://github.com/tochi-mba/rexplayer/commit/982eb9bf27566657bde3272bf5aaf160436898ba) · [Tests](https://github.com/tochi-mba/rexplayer/commit/a22331711de2b06cdd537273b7f4071afeef17e8) · [Groups](https://github.com/tochi-mba/rexplayer/commit/4c9cc58450604352787294d296cc678d3522671a)
- [x] Add a full, inert, keyboard-readable show-notes dialog and cap show description previews, with Windows UI assertions. [Show](https://github.com/tochi-mba/rexplayer/commit/e3fbd39ef7d53c2a09e6e1d9b5e8ab75dfdab3ec) · [Test](https://github.com/tochi-mba/rexplayer/commit/3ef1e7873c7909d47324e04739e01fbd7fabf77e)
- [x] Add bounded asynchronous local episode-notes previews to only realized rows, preserving stable AutomationNames; test live OneWay detail updates and explicit full-notes action. [Policy](https://github.com/tochi-mba/rexplayer/commit/b0fca5ff4b2c8d5d7bd58bc82794202d66ea4ba2) · [Row](https://github.com/tochi-mba/rexplayer/commit/0d08ac7b3efc898f1922a11858ae2d0d4fbdea0b) · [UI test](https://github.com/tochi-mba/rexplayer/commit/57c18cdbff83235f69d2be61ceff1719ed207672)
- [x] Add speech decoding regression cases for mono/stereo/three-channel silence, clipped audio and zero-sample input, on top of the waveform geometry tests. [Test](https://github.com/tochi-mba/rexplayer/commit/42a9d214f761183e485240b51706bd996ef3a8a5) · [Fixture correction](https://github.com/tochi-mba/rexplayer/commit/f75362fcb8a472529804b9b3ae1d92b25a56d63c)
- [x] Cover partial live speech via a real cached partial-transcript UI Automation scenario; show incomplete status and only timed paragraphs that were actually recognized. [UI test](https://github.com/tochi-mba/rexplayer/commit/de89af643d8ff86d413cc72c553d4238de6fc81d)
- [x] Cover a real moved episode file with local disk existence checks; exclude both still-downloading and known-damaged files from the unplayed queue. [Pure test](https://github.com/tochi-mba/rexplayer/commit/f8eec368fb323b88c6dd153dfb32e78fc60d6ff3)
- [x] Commit actual Windows capture hooks for podcast Home, show detail, recording groups, light/dark/compact states and partial-speech modal. Actual CI screenshots and reviewer-approved baselines are still outstanding. [Windows fixtures](https://github.com/tochi-mba/rexplayer/commit/b1c30d751f5ee14c3a6adea6773526228bec3035) · [Light/dark](https://github.com/tochi-mba/rexplayer/commit/096a819fb4ded2b810040edd1473ff37cadb3fe3) · [Partial transcript](https://github.com/tochi-mba/rexplayer/commit/325ad6c677a5867b78c7e07e7b5d88d34897fed7)
- [x] Label local transcription's edited, partial and failed states in the speech reader and assert failure reason with saved partial words in native UI test. [Reader](https://github.com/tochi-mba/rexplayer/commit/18c8f91310dabb0fbbcfa1eaa94690c03a82be2a) · [Test](https://github.com/tochi-mba/rexplayer/commit/43fe4dc8f75b6cd7e4e3611a3fccc9bdb061e68a)
- [x] Review newly committed code for exact index/type and nullable sidecar safety; fix chapter enumeration and checked non-null local notes paths. [Chapters](https://github.com/tochi-mba/rexplayer/commit/abc02d6a7ff50ef29469a03626f1f246fc9711d5) · [Notes](https://github.com/tochi-mba/rexplayer/commit/0a0ad1a179072064277c191de8c9c01e9ffef25d)
- [ ] Verify the pure suite and required 100% portable coverage on this branch head.
- [ ] Verify warnings-as-errors Windows build, real UI Automation, screenshot, accessibility, performance and platform/parity checks at the final head.
- [ ] Complete the PR #484-owned scope and required release gates; inherited tasks assigned to sibling successors are excluded from this PR's completion claim.

**Verification limitation:** No .NET SDK or Windows desktop runtime is installed in the editing environment, so local compilation and native UI execution were not possible. GitHub Actions was pending at the time of these edits; a pending run is not passing evidence. No generated screenshots are being passed off as real application captures.

## Historical workstream checklist — 63 original roadmap tasks
The original granular text is preserved as a **traceability appendix**, not 63 tasks assigned to PR #484. Of the categories below, **“Podcasts and spoken-word transcript UX”** and **“Recordings and short sounds”** are the owned implementation work. Checked owned items mean the corresponding source and regression-test code has been committed, **not** that CI or Windows acceptance has passed; all release gates remain open. Other sections are delegated to sister PRs and their checkboxes remain untouched.

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

- [x] Podcast Home: show-oriented square art, continue, unplayed/recent local episodes and date-aware organization.
- [x] Podcast show detail: publisher/description/episodes, consistent sort, progress state, local-only messaging and missing-art fallback.
- [x] Episode row: title, published date if reliable, duration, remaining time, status, short/expanded notes, Play/Resume and transcript indicator.
- [x] Podcast player prioritize ±seconds, episode progress, speed, sleep timer, queue and chapter/note/transcript panels where available.
- [x] Transcript view use sentences/paragraphs with timed speech highlighting, manual-scroll suspension and optional query navigation; not music-lyrics karaoke layout.
- [x] Show notes must handle long markup safely: no HTML/script execution, safe links, truncation and keyboard-readable text.
- [x] Queue 'play unplayed in order' uses actual availability; never autoplay a different show's episodes unexpectedly.
- [x] Keep RSS feed subscription, built-in downloads and podcast network features gated by existing `PLF-08`, `NET-15` and relevant `NET-*`; do not draw live Subscribe/Download controls prematurely.
- [x] Test local shows with 1/1000 episodes, no dates/art, identical episode names, incomplete transcripts, offline and stopped downloads.

### P1. Recordings and short sounds
Existing record-library browsing is separate from future capture/record functionality.

- [x] Recordings: date sections (Today/Yesterday/This Week/older/undated), voice-note group, compact rows, source/date/duration and correct local-file actions.
- [x] Waveform detail when added: analysis off UI thread, bounded cache, seek/zoom/pan/time ruler/current playhead and cancellation on media switch.
- [x] Recording details: filename/title, date, duration, path, optional waveform, transcript, bookmarks and clear remove-from-library versus delete-file semantics.
- [ ] Transcript mode: search/jump to timed speech if indexed, manual-scroll suspension and clear partial/edited/transcription-error status.
- [ ] Review mode: frame-irrelevant audio tools, exact seek and playback speed; use existing `PB-08` for future A-B loop rather than inventing duplicate capability.
- [x] Short sound mode: compact title/duration, replay, repeat and volume; no enormous book cover or unnecessary full animation.
- [ ] Optional true capture uses existing `AU-24`/`OUT-06`/`CAP-*` rows and separate permission/source/record/pause/stop/format/storage/error workflows; do not conflate with playing recordings.
- [x] Test 0-second, hours-long, silence, clipped loudness, mono/stereo/multichannel, overlapping jobs, moved files, missing timestamps and enormous transcript.

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

## Related work
- [#467](https://github.com/tochi-mba/rexplayer/pull/467): original four-part Windows roadmap, kept as historical trace in this file.
- [#473](https://github.com/tochi-mba/rexplayer/pull/473): successor split with separate audiobook and metadata/tools implementation owners.
- [#484](https://github.com/tochi-mba/rexplayer/pull/484): **this successor**, podcasts, speech transcripts, recordings and short sounds.

**Merge gate:** PR #484 remains **not ready to merge** until the owned implementation, reviewable Windows screenshot/automation/accessibility evidence and green current-head CI exist. A checked commit checkpoint is not a shipped feature.
