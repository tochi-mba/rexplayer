# Unfinished-checklist audit for already-merged PRs

> **Status:** unverified, not completed. This inventory records unchecked items in the source PR descriptions as of 2026-10-23. An unchecked item is not proof that all underlying code is absent, but it is **not acceptance evidence** either. No item may be checked solely because a PR merged or CI was green.

## Scope and rules

- **In scope:** only merged PRs #481, #485, #482, #479, #487, #486, and #468. Do not add work from PRs that remain open, except where a scoped task explicitly depends on it.
- **Baseline:** 69 unchecked source checklist entries across 7 merged PRs. Repeated items remain listed for traceability rather than silently counted as delivered.
- Each item must have a commit/diff proving implementation, focused tests, required platform CI, accessibility / performance evidence where applicable, and a human screenshot or native-device verification where expressly required.
- Keep the replacement follow-up PR **draft** until all required gates are satisfied. Do not merge on GitHub mergeability or green CI alone. If native hardware, finalized release media, or live deployment is necessary, leave that item unchecked until verified.
- Task #482.10 was a **historical pre-merge gate** that cannot be retroactively satisfied; record that violation and apply the analogous draft/approval gate to this replacement PR.
- Website tasks #468.5, #468.6 and #468.10 overlap. Validate published GitHub Pages on desktop and narrow mobile *after* the now-merged gallery is deployed, not only a local Pages-equivalent server.
- Release screenshots, Mac screenshots, and rendered-caption/movie screenshots in #468 must be real outputs of built/released apps, with provenance and visually reviewed. Existing synthetic CI fixtures do not satisfy that requirement.
- Keep capability matrix and public site claims conservative; absence of implementation evidence is not a verified capability.

## Verification ledger

Columns for evidence should be added next to checked entries only after the evidence exists. All source tasks began unchecked in this follow-up PR; independently verified items are marked individually below with links to their evidence.

### [#481](https://github.com/tochi-mba/rexplayer/pull/481): Split Windows music library browsing and discovery roadmap (12)

- [ ] **481.1** Build Music Home from real local-library state with honest recent, most-played, pinned, and empty sections.
- [ ] **481.2** Complete songs browsing with sortable columns, virtualization, selection, queue actions, and missing-file behavior.
- [ ] **481.3** Complete album pages with reliable grouping, disc and track ordering, artwork fallback, and progress where meaningful.
- [ ] **481.4** Complete artist pages without merging ambiguous artists or inventing biographies.
- [ ] **481.5** Complete genre browsing while handling missing, multiple, malformed, and localized genre values.
- [ ] **481.6** Preserve playback, selection, scroll position, and route while metadata completes.
- [ ] **481.7** Handle huge libraries, duplicate titles, Unicode, RTL, missing art, unavailable files, and disconnected storage.
- [ ] **481.8** Keep network enrichment and download controls behind verified capabilities.
- [ ] **481.9** Add pure tests for grouping, ordering, identity, and fallback policy.
- [ ] **481.10** Add Windows UI automation and deterministic screenshots for every principal and edge state.
- [ ] **481.11** Run pure coverage, Windows build, UI automation, accessibility, screenshot, performance, and warnings-as-errors gates.
- [ ] **481.12** Update capability and UX documents only when evidence exists.

### [#485](https://github.com/tochi-mba/rexplayer/pull/485): Split Windows metadata, transcription tools, preferences, and integration (12)

- [ ] **485.1** Implement safe metadata editing with provenance, mixed values, validation, deferred writes, and progress/error feedback.
- [ ] **485.2** Implement artwork preview, override, reset, resolution/aspect feedback, and explicit source-write consent.
- [ ] **485.3** Keep refresh non-blocking and recover missing, moved, disconnected, and inaccessible library items without deleting history.
- [x] **485.4** Design transactional backup/restore and honest incomplete-file state. **Verified design deliverable:** [transactional snapshot/export/restore, rollback and truthful incomplete-file architecture](../architecture/library-backup-restore-contract.md) grounded against current `LibraryBackup`, `MediaLibrary`, `RexStore`, `AtomicFile` and `Downloads` sources. **Important:** this marks the *design* complete, **not** the unimplemented transaction or its Windows UI validation; those implementation and quality gates remain open.
- [ ] **485.5** Refine timeline seeking, elapsed/remaining time, hover feedback, buffered or transcript rails, markers, and keyboard use.
- [ ] **485.6** Preserve primary and secondary subtitles, delay, language, styling, realistic previews, and high contrast.
- [ ] **485.7** Complete transcript editing and job states with timing, partial coverage, search, model state, cancel, export, and disk errors.
- [ ] **485.8** Complete equalizer, picture effects, subject lock, technical information, logs, diagnostics, and privacy-aware export.
- [ ] **485.9** Audit advanced preference sections, hotkeys, first run, About, What's New, updater states, dialogs, and validation.
- [ ] **485.10** Preserve native title bar, Snap, file associations, taskbar, SMTC, Jump List, DPI, theme materials, and minimal-window behavior.
- [ ] **485.11** Keep future network, capture, output, and advanced playback controls hidden until dependencies are verified.
- [ ] **485.12** Add focused pure tests, Windows UI automation, accessibility evidence, deterministic screenshots, performance checks, and warnings-as-errors.

### [#482](https://github.com/tochi-mba/rexplayer/pull/482): Split Windows audio visualizer compatibility and persistence (10)

- [x] **482.1** Inventory every visualizer mode, option, default, and persistence key against the current implementation. **Verified source-inventory deliverable:** [exact keys, 42 modes, default palettes, 146 specific options, ranges and all choices](visualizer-option-inventory.md) against `VisualizerChoice`, `VisualizerOptions.For`, and `VisualScene.Draws`; regression test `EachModeOptionHasAValidDefaultAndAStableDistinctPersistenceKey`. This does **not** close tasks #482.2–#482.9 (native controls and end-to-end quality checks).
- [ ] **482.2** Verify switching modes preserves compatible values and resets incompatible values predictably.
- [ ] **482.3** Keep analysis off the UI thread and bound memory, CPU, and cancellation behavior.
- [ ] **482.4** Verify pause, seek, track change, audio-device change, window resize, minimize, and shutdown transitions.
- [ ] **482.5** Provide keyboard and assistive-technology access to every visualizer control.
- [ ] **482.6** Handle silent audio, mono, multichannel, clipped levels, very short files, and unavailable analysis.
- [ ] **482.7** Capture deterministic screenshots for representative modes, settings, compact width, high DPI, and high contrast.
- [ ] **482.8** Add focused pure tests for any new policy and Windows UI tests for actual controls.
- [ ] **482.9** Run pure coverage, Windows build, UI automation, accessibility, screenshot, performance, and warnings-as-errors gates.
- [ ] **482.10** Keep the PR draft until native visual review and current-head CI are complete.

### [#479](https://github.com/tochi-mba/rexplayer/pull/479): Split Windows movie and episodic library behavior (8)

- [ ] **479.1** Complete movie browsing with honest metadata, artwork fallbacks, progress, resume, and local-file actions.
- [ ] **479.2** Complete movie detail and cast surfaces without inventing unavailable network metadata.
- [ ] **479.3** Complete show, season, and episode browsing with stable sorting and progress semantics.
- [ ] **479.4** Distinguish episode completion from season and series completion.
- [ ] **479.5** Handle specials, missing season numbers, duplicate episode numbers, multi-episode files, moved files, and damaged files.
- [ ] **479.6** Verify resume targets after files disappear or metadata changes.
- [ ] **479.7** Add Windows UI automation and deterministic screenshots for empty, populated, partial, and error states.
- [ ] **479.8** Run pure coverage, Windows build, UI automation, accessibility, screenshot, performance, and warnings-as-errors gates.

### [#487](https://github.com/tochi-mba/rexplayer/pull/487): Split enhanced timed lyrics parsing and navigation (8)

- [ ] **487.1** Complete display states for no lyrics, untimed text, line timing, word timing, partial timing, loading, and errors.
- [ ] **487.2** Suspend automatic scrolling after manual navigation and provide a return-to-active-line action.
- [ ] **487.3** Preserve timing and focus across seek, pause, speed changes, track changes, and resize.
- [ ] **487.4** Complete detached companion ownership, close/reopen, multi-monitor, scaling, and focus behavior.
- [ ] **487.5** Verify long lines, Unicode, RTL, repeated timestamps, metadata tags, and malformed files.
- [ ] **487.6** Add Windows UI automation and deterministic screenshots for every lyric mode.
- [ ] **487.7** Run pure coverage, Windows build, UI automation, accessibility, screenshot, performance, and warnings-as-errors gates.
- [ ] **487.8** Keep unsupported online retrieval and editing controls capability-gated.

### [#486](https://github.com/tochi-mba/rexplayer/pull/486): Split Windows shell, library layout, and Home foundation (9)

- [ ] **486.1** Finish adaptive shell behavior at minimum, compact, standard, wide, high-DPI, and high-contrast configurations.
- [ ] **486.2** Complete Home with honest Continue, recent, pinned, empty, loading, and error states backed by local data.
- [ ] **486.3** Unify browsing patterns across the Windows library routes.
- [ ] **486.4** Preserve keyboard, mouse, touch, controller, and assistive-technology navigation.
- [ ] **486.5** Handle long Unicode text, missing artwork, huge libraries, disconnected locations, and RTL layout.
- [ ] **486.6** Keep route and selection stable during background metadata and indexing.
- [ ] **486.7** Add deterministic fixtures and screenshots for every changed shell and Home state.
- [ ] **486.8** Run pure, Windows build, UI automation, accessibility, screenshot, performance, and warnings-as-errors gates.
- [ ] **486.9** Update capability and UX claims only when implementation evidence exists.

### [#468](https://github.com/tochi-mba/rexplayer/pull/468): feat(site): real Windows screenshots, accessible gallery, and honest Mac parity (10)

- [ ] **468.1** Replace test-fixture screenshots with curated authentic captures from a finalized release for both platforms.
- [ ] **468.2** Add genuine macOS screenshots once the Mac library and Up Next implementations are build-verified.
- [ ] **468.3** Add visual previews of now-playing, Settings, rendered captions, first run and TV/movie detail pages when the corresponding native screenshots are available.
- [ ] **468.4** A captured now-playing state, rendered captions and detailed movie view (not available as suitable release-quality native evidence yet).
- [ ] **468.5** Validate all imagery loads and looks correct on GitHub Pages, including narrow mobile widths.
- [ ] **468.6** Live GitHub Pages appearance on narrow mobile and desktop: requires deployment after merge, not equivalent to the local Pages test server.
- [ ] **468.7** **Release-media gate:** capture and visually review representative native Windows and Mac screenshots from actual finalized builds, with recorded versions and correct cross-platform labels. Do not mark test fixtures or generated illustrations as satisfying this.
- [ ] **468.8** **Mac-screen gate:** implement and build-verify Mac library and Up Next first; only then capture and integrate truthful screenshots (and update `docs/capability-matrix.json` as required by parity policy).
- [ ] **468.9** **Native-screen gate:** obtain true now-playing, rendered-caption and movie-detail captures and verify their visual quality before promoting them on the site.
- [ ] **468.10** **Deployment gate:** verify the PR's images and layout on *deployed* GitHub Pages at desktop and narrow-mobile widths. Pages currently deploys on `main` pushes; `main` does not contain this PR's gallery, so testing the live URL now cannot certify the PR changes. Use a safe preview deployment, or explicitly track production validation after merge.

## Acceptance and merge gate

- [ ] No in-scope task is left unchecked without a documented owner-approved scope change, and no item is checked without code/test/evidence references.
- [ ] Run current-head portable tests and coverage, Windows build and native UI automation, macOS build and native smoke where relevant, accessibility, screenshots with visual review, performance baseline, and warnings-as-errors.
- [ ] Verify the production website screenshots and claims against finalized release binaries and deployed Pages at narrow/mobile and desktop widths.
- [ ] Review all PR comments, check native focus/keyboard/assistive-technology behavior, and confirm no blocking CI failures before asking for review.
- [ ] Re-read this checklist end to end before changing the replacement PR from draft to ready; do not auto-merge.

## Work implemented in this follow-up branch (verification pending)

These changes are **partial** evidence for the corresponding entries, not permission to check off their broader end-to-end acceptance criteria:

- **#487.5:** LRC offset parsing now ignores numbers that do not fit into a signed millisecond offset; line timestamp parsing ignores seconds outside 0-59. Regression tests cover damaged offsets, malformed seconds, long Unicode/RTL lines, repeated time marks and metadata tags. Still requires current-head CI and native UI validation for the full lyric workstream.
- **#479.5:** episode sibling grouping no longer treats a combined S01E01E02 file and a standalone S01E02 file as two independent siblings. Regression test covers overlapping ranges. Other multi-file, moved-file, and damaged-file behavior is still outstanding.
- **#482.1:** `visualizer-option-inventory.md` enumerates all 42 declared modes, 146 mode-specific options, shared settings, default palettes and the mode-qualified persistence key convention. Added pure regression coverage for default values and distinct persistence keys. This does not validate rendering, assistive technology, transitions or hardware performance.

Nothing in this section certifies human-reviewed native screenshots, accessibility, live Pages, or a finalized release. Keep checklist entries unchecked until their complete requirements and test gates are verified.
