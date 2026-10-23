<!-- Scoped workstream source of truth. Tasks are intentionally unchecked until evidence-backed. -->

# Windows UX workstream 1/4  -  Foundation, Settings, shell and Home

## Objective and deliverables
Create the shared Windows design system, adaptive shell, robust Home/browse patterns and trustworthy UI screenshot evidence. Preserve and complete the existing Settings/category-search and screenshot-guard implementation in the original PR.

This is one of **four evenly partitioned draft implementation workstreams**, carved out of [original PR #467](https://github.com/tochi-mba/rexplayer/pull/467). Each owns **63 unique unchecked tasks from the original roadmap**, plus exactly eight identical release gates. Equal checkbox counts are a planning balance, not a guarantee of equal elapsed implementation time. Tasks are **not completed** merely because a checklist has been transferred.

## Scope and implementation owners
**In scope:** The native window and Settings foundation, title bar, navigation, Home, cross-library browsing patterns, deterministic media fixtures and screenshot harness.

**Likely source/test owners:** `MainWindow.Preferences.cs`, `MainWindow.xaml`, `Library*Templates.xaml`, the shell/Home UI files, `Screenshots.cs` and the Windows UI tests.

**Out of scope:** unrelated audio/video decoding, analytics/network additions, breaking AutomationIDs or hotkeys, unverifiable macOS parity claims, and sister-PR tasks. Shared source changes must be coordinated before merge.

## Honest current delivery state
- [x] The six existing source/test/CHANGELOG changes from original #467 are preserved **verbatim** on this branch: adaptive Settings rail, category/control search and feedback, screenshot variation guard, regression and visual smoke tests.
- [x] The earlier [Windows warnings-as-errors build, formatting, coverage and other CI jobs](https://github.com/tochi-mba/rexplayer/actions/runs/39984802547) succeeded on the former head.
- [ ] Recheck the new original head `d176f950` on [CI](https://github.com/tochi-mba/rexplayer/actions/runs/39993169196); the previous run had 178/179 UI tests passing, with a search-recovery failure now being addressed. Do not claim the current head passes until evidence exists.
- [ ] Verify native keyboard navigation, screen readers, high contrast, compact/normal/ultra-wide Settings, and review visual baselines.
- [ ] Complete the additional 63 assigned roadmap tasks. Existing Settings/screenshot code **does not** constitute delivery of the future shell, Home and fixture overhaul.

## Detailed task list  -  63 uniquely owned roadmap tasks
The original, granular task text is reproduced so scope is not lost. No future checkbox was silently marked complete.

### P0. Repository audit, scope and nonduplication
Start by grounding every implementation PR in the actual Windows app, not a visual concept. Preserve the completed work and existing automation contracts.

- [ ] Record the exact head SHA, current CI, PR #467 changed files, outstanding reviews, existing UI routes, menus, dialogs, flyouts, keyboard shortcuts and automated screenshots before touching implementation.
- [ ] Treat the checked Settings-category rail/search and blank/uniform screenshot-guard items above as **already implemented in this branch**; do not rebuild or re-count them.
- [ ] Verify the current PR head with Windows x64 build, formatting, pure tests, adapters, UI suite, latest screenshot artifacts and manual Settings search/layout review; only tick the preceding outstanding items after actual evidence.
- [ ] Inventory all `MainWindow.*.cs` partials, `MainWindow.xaml`, library XAML templates, `AppCore` view-selection policies, settings model, capability JSON, `AGENTS.md` and test harness.
- [ ] Enumerate every route and state: initial window, playback stage, library Home, Music, Songs, Albums, Artists, Genres, Audiobooks, Authors, Podcasts, Recordings, Movies, TV Shows, Other/All Videos, Pictures, Continue Watching, Completed, Recently Played/Added, Playlists and Folders.
- [ ] Enumerate every transient view: menu bar, context/overflow menus, info flyouts, player overlays, toast/status banners, selection surfaces, Settings subsections, first run, What's New, hotkey editor, updates, metadata/artwork dialogs, transcription, equalizer, visualizer settings, Subject Lock, picture styles and diagnostics.
- [ ] Map requirements to existing capability IDs before allocating new ones; distinguish `verified`, `built`, `planned` and `post-1.0`, never infer verified from source-code presence.
- [ ] Create an owner-friendly per-surface audit: current implementation paths, entry conditions, screenshot IDs, defects, proposed treatment, source capability, tests and acceptance proof.
- [ ] Separate **current PR implementation** from **future unchecked roadmap**: one bounded follow-up PR per workstream; do not silently convert this six-file Settings/screenshot PR into an unreviewable monolith.
- [ ] Do not name competitor products or their organizations anywhere in PR/repository text; follow the repository's clean-room, platform, dependency, testing and commit policies.

### P0. Real Windows screenshot evidence and deterministic fixtures
The existing screenshot infrastructure is the starting point. New images must come from the running Windows app, with visual inspection, behavior oracles and approved CI baselines.

- [ ] Add a declarative capture inventory mapping `scene-id → fixture → route → state → dimensions → theme → expected automation IDs → expected visible data → readiness signal → screenshot owner`.
- [ ] Build deterministic, offline fixture libraries: one item, sparse, mixed media, artwork-rich, artwork-free, huge, corrupt, Unicode/RTL, duplicates, slow/partial files, deep folders, episodic shows, multipart books, varied podcast dates, long recordings, unusual pictures.
- [ ] Capture the same scene at 640×480, 720×640, 800×600, 1000×760, 1280×720, 1440×900, 1920×1080, 2560×1440, 3440×1440 and 3840×2160 as relevant; test minimum viable dimensions, not assume all modes fit.
- [ ] Cover logical sizing and 100/125/150/175/200% DPI, maximized/restored/snapped/fullscreen, short/tall windows, mixed-DPI multiple monitors and offscreen-window recovery.
- [ ] Capture normal, selected, hovered, keyboard-focused, context-menu, loading, partial metadata, empty, filtered-empty, no artwork, missing file, disconnected storage, damaged media, playback, paused and failed states.
- [ ] Capture light/dark/high-contrast, system accent changes, 200% text scaling, reduced motion, touch and keyboard focus, long CJK/RTL names and right-to-left text layout.
- [ ] Extend `Screenshots.Match(app,...)` with per-scene readiness, metadata and useful failure artifacts without duplicating the current `HasVisibleVariation` preflight.
- [ ] Keep baseline ownership under `tests/screenshot-baselines/ci-windows/`; do not use developer images as CI baselines and do not declare a never-baselined scene visually verified.
- [ ] Publish actual, approved baseline, diff, crop and test metadata as review artifacts; every intentional baseline update needs human explanation.
- [ ] Build screenshots of the *actual* app, not generated design mocks; use before/after images and independently verified UI Automation assertions.
- [ ] Avoid arbitrary sleeps/retries; use observable UI conditions, fixture stability, predictable animation state and offline metadata; never mask flicker or stale artwork by over-waiting.
- [ ] Detect screenshots with clipped controls, hidden focus, unreadable artwork contrast, overlapped captions, offscreen popups, wrong image ratio, false currently-playing state and empty unpopulated fixture.
- [ ] Expand visual QA in phases so CI cost and asset sizes remain manageable; sample enormous permutations with risk-focused pairwise suites, but always include every distinct layout and failure class.

### P0. Shared Windows design system and adaptive shell
Create one original RexPlayer visual language with media-specific treatments, not a common generic card imposed on every file.

- [ ] Centralize XAML token resources for typography, spacing, radii, surfaces, layer hierarchy, semantic text, dividers, focus rings, interaction states, artwork aspect ratios, shadows and motion.
- [ ] Audit fixed XAML sizes (e.g. 200px navigation, 344px queue, hero/card constants), define actual min/max/adaptive behavior and avoid both oversized empty space and squeezed controls.
- [ ] Unify WinUI 3 title bar, drag region, native min/max/close and Snap layout support with the main navigation, content and transport; preserve genuine Windows feel.
- [ ] Group existing sidebar destinations into meaningful Home/activity, Music, Listening, Watch and Personal Library categories only where useful; preserve conditional presence and compact icon navigation.
- [ ] Keep collapsed sidebar glyphs distinguishable, tooltips/automation available, current location obvious and selection stable during folder scan or category changes.
- [ ] Preserve Back and scroll/focus/selection/search/sort/filter state across album, artist, book, show/season, movie/person, playlist and media-details navigation.
- [ ] Review queue and contextual pane composition: optional resize, responsive overflow, maximum width, focus restoration, minimum stage width and no conflicting side-panel stacks.
- [ ] Rationalize native Media/Playback/Audio/Subtitles/Video/View/Help menus and contextual action locations without removing existing command shortcuts or automation IDs.
- [ ] Make primary actions always discoverable without hover, with equivalent keyboard/focus affordances and correct disabled explanations.
- [ ] Provide distinct empty/loading/error/unavailable/action-required view templates that never imply media can be played when it cannot.
- [ ] Use artwork-derived tint only on appropriate local surfaces, with safe contrast, deterministic fallback and protection from stale async art callbacks.
- [ ] Prefer compositional UI components and media-specific templates over ballooning `MainWindow.xaml` with hundreds of visibility branches.
- [ ] Require all new style resources to work in light, dark and high contrast; avoid fixed foregrounds except carefully controlled media overlays.

### P0. Home and universal browsing
Improve the existing shelves, Browse/list/card/collage views and existing controls rather than reimplementing them.

- [ ] Home: emphasize Continue Watching/Listening, contextually recent media, useful newly added items, frequented albums/playlists and safe local rediscovery; avoid duplicate shelves or fabricated recommendations.
- [ ] Home: mixed library, single-type library, empty/new library, large library and indexing states; never require online metadata or account creation.
- [ ] Keep hero/shelf/See All composition balanced on short windows; avoid giant billboards swallowing the primary content.
- [ ] Improve Browse shelves' horizontal scroll, mousewheel/trackpad, touch, focus escape, keyboard arrows, edge affordance and restored shelf position after details navigation.
- [ ] Audit existing list, browse, cards, collage, video thumbnails, photo grids and grouped rows; ensure media-specific aspect ratios and accurate overlays.
- [ ] Rework browse toolbar presentation (look, sort, grouping, filters, size) for visible active state, clear/reset filters, overflow on narrow views and persistent per-view choices.
- [ ] Improve stable sorting, natural numbering, multi-disc order, grouping toggles, sticky headers only if compatible with virtualization and scroll anchoring.
- [ ] Improve Songs-style dense rows separately from posters, episodes, podcasts and files; allow compact/comfortable density without destroying keyboard selection.
- [ ] Ensure Ctrl/Shift multi-select, Select All, right-click selected/unselected, drag/drop, multi-item actions and mixed-media selection refer to correct item identities after async updates.
- [ ] Improve library search scope/hints, group versus individual results, matched fields, no-results explanation, keyboard focus, query clearing and return navigation; do not duplicate Settings search.
- [ ] Folder browser: breadcrumbs, watched-folder state, nested directories, paths, show-in-Explorer, add/remove/rescan/backup/restore and permission/disconnect states.
- [ ] Preserve lazy artwork loading, prioritized visible/focused cards, cancellation of recycled items and virtualized rows during all new layouts.
- [ ] Set reproducible UX/perf gates for 3,000/20,000/100,000 fixture items, initial render, scrolling, search, sort, resize, memory, cache churn and artwork mismatches.

### P0. Edge-case matrix and quality gates
Each item must be a test or an explicit reviewed exclusion, never silently assumed safe.

- [ ] Artwork: late callback for previous route, recycled card showing wrong thumbnail, aspect/stretch regression, low-res image, alpha transparency, damaged cache, change while visible.
- [ ] UI: 640×480 or viable minimum, extreme narrow/short, RTL, 200% font, 200% DPI, ultrawide/4K, high contrast, accent change, focus loss, reduced motion, narrator and touch.
- [ ] Error/recovery: no blank windows, no stale song/show identity, no silent failed action, no unrecoverable modal, no partial PR feature presented as finished.

### P0. Architecture, tests and per-PR execution contract
Each future agent should take one unchecked workstream, select bounded tasks and deliver one independently reviewable follow-up PR.

- [ ] Implement policy/model in pure tested AppCore code; Windows XAML and window orchestration remain in the app shell. Keep decoders/adapters untouched for purely visual work.
- [ ] Do not mix unrelated features into a single follow-up change; use imperative non-conventional commit subjects and rebase-merge discipline.
- [ ] Add pure policy/view-model tests and keep any new pure source under required 100% line coverage (`tests/coverage-required.txt`), with deterministic time and no wall-clock sleeps.
- [ ] Use `./dev.ps1 test`, `./dev.ps1 gate`, Windows `./dev.ps1 build`, `./dev.ps1 ui`, `./dev.ps1 adapters`, `./dev.ps1 perf` and `./dev.ps1 measure -LowEnd` where impacted.

### P0. Capability matrix, generated parity and documentation protocol
Source of truth is `docs/capability-matrix.json`. Generated files are never hand edited. The Windows-only UI project does **not** grant permission to claim cross-platform completion.

- [ ] For every task, first classify: (a) style/interaction refinement of existing verified functionality, (b) implementation of an existing planned ID, or (c) genuinely new capability with its own stable ID.
- [ ] Each new matrix row: area/name/details, milestone/priority, oracles, Windows status, macOS status, shared-core applicability, and accurate gap note/issue when policy requires.
- [ ] Read both `AGENTS.md` and ADR-0020 before landing Windows-only parity changes: a feature lands on both shells or uses an accurately documented/accepted gap; do not misuse `not-applicable` simply because macOS UI is out of scope.
- [ ] Update `docs/architecture.md`, `docs/specs.md`, `docs/testing.md`, `docs/fixtures.md`, `docs/library-classification.md`, ADRs, README and CHANGELOG only when the change actually affects their contracts.

### Required screenshot families (expand into stable fixture-backed IDs)
- [ ] shell: empty/light/dark, video/audio, library/sidebar open/compact, queue, snapped/narrow/tall/ultrawide, high contrast, 200% text, focused menu, fullscreen, minimal
- [ ] Home: empty/music-only/movie-only/mixed, continue, recently added, loading, no artwork, card hover/focus, search and partial indexing
- [ ] Other: Settings each category normal/compact, hotkey editor, first run, update/error, What's New, About, backup/restore, missing-file recover, queue, playlist, diagnostics

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
- **1/4  -  UI foundation and deterministic QA (this PR)**
- 2/4  -  Video and adaptive playback
- 3/4  -  Music, lyrics and visualizers
- 4/4  -  Spoken word, metadata and tools

**Merge gate:** Remain draft until actual implementation, full task evidence, reviewed real screenshots and green current-head CI exist. A planning document is not a completed feature. Original complete source and roadmap remain traceable in [#467](https://github.com/tochi-mba/rexplayer/pull/467).
