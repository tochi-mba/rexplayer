<!-- Scoped workstream source of truth. Tasks are intentionally unchecked until evidence-backed. -->

# Windows UX workstream 2/4 · Movies, TV, video, pictures and playback

## Objective and deliverables
Rework Movies, TV, episodes, pictures and the actual video player around media-specific layouts and trustworthy playback/navigation states without replacing the media engine.

This is one of **four evenly partitioned draft implementation workstreams**, carved out of [original PR #467](https://github.com/tochi-mba/rexplayer/pull/467). Each owns **63 unique unchecked tasks from the original roadmap**, plus exactly eight identical release gates. Equal checkbox counts are a planning balance, not a guarantee of equal elapsed implementation time. Tasks are **not completed** merely because a checklist has been transferred.

## Scope and implementation owners
**In scope:** Movies, cast/person details, episodes, seasons, Other/All Videos, pictures/slideshows, contextual queue/playlist behavior and media-adaptive playing stage.

**Likely source/test owners:** Movie/TV/pictures XAML and view code, `MainWindow.Presentation.cs`, video/player layout policies and their Windows UI Automation/screenshot tests.

**Out of scope:** unrelated audio/video decoding, analytics/network additions, breaking AutomationIDs or hotkeys, unverifiable macOS parity claims, and sister-PR tasks. Shared source changes must be coordinated before merge.

## Honest current delivery state
- [x] Transfer these requirements to an independently owned, versioned scoped plan in this branch.
- [ ] **Complete implementation and verification.** A first TV-continuation and media-stage policy slice is committed; the remaining roadmap and release gates are not complete.
- [ ] Attach actual Windows captures and native CI results before claiming feature readiness.

## Implementation progress · 2026-10-23
This is a partial implementation, **not** acceptance of any full roadmap or release gate. The existing 63 roadmap checks stay open until their entire scope is proven.

- [x] Fix pure next-episode selection so missing season numbers do not prevent playing an available later episode (commit `23139d1`).
- [x] Add deterministic regression assertions for season gaps, unordered candidates, specials and different shows (commit `c3b5eff`).
- [x] Prevent unreadable or missing-status episode rows from becoming the show's Play/Resume target; preserve their visible error rows (commit `2efbf23`).
- [x] Add deterministic tests for corrupt/missing resume targets, Season 0 ordering and all-unreadable shows (commit `74e683a`).
- [x] Add a pure stream-first media stage/profile and capability resolver without changing the player/decoder session (commit `a6d9185`).
- [x] Wire stage labels, audio art/lyrics eligibility and stage UI Automation name to active streams rather than stale library-only labels (commits `07a427c`, `f9da1e8`).
- [x] Add pure stream/profile coverage tests and require full line coverage for the new policy (commits `1612acc`, `ddbeb49`).
- [x] Write a real Windows UI Automation test of song → movie → picture stage transitions (commit `296e939`); execution and screenshot proof remain pending.
- [x] Withhold lyrics and chapters until the current audio stream has been identified, including regression assertions for stale stream transitions (commits `277cf8f`, `ae47713`).
- [ ] Execute the affected pure tests on a .NET-capable runner; they have **not been run here**.
- [ ] Complete the remaining TV browsing, player, picture, queue, adaptive layout, automation, screenshots and performance items before declaring readiness.

## Detailed task list · 63 uniquely owned roadmap tasks
The original, granular task text is reproduced so scope is not lost. No future checkbox was silently marked complete.

### P0. Movies, cinematic details and cast
Movies already have Browse billboards, cards, metadata, details, cast/person pages, related shelves and version selection. Polish those, do not invent duplicate routes.

- [ ] Movie Browse: cinematic but efficient billboard, clear title/year/runtime/genre facts, proper Play/Resume/More Info and safe contrast for any backdrop.
- [ ] Preserve featured-card hold/pause on pointer and keyboard focus, reduced-motion preference and stability during metadata updates/resizing; do not autoplay sound or trailers.
- [ ] Refine poster-grid/list/collage variants, selected/now-playing/progress badges, explicit focus action, poster-free fallback and dark/light/high-contrast readability.
- [ ] Movie shelves: continue, added, unwatched, genre/decade/series/director only where trustworthy local data exists; remove empty and duplicated rows.
- [ ] Details: balanced backdrop/poster/title/synopsis/primary action hierarchy, accessible expandable biography/description, optional credits, local-only offline mode.
- [ ] Cast/crew: correct portrait placeholders, character/credit labels, person-page navigation, locally owned media differentiation and duplicate-name ambiguity.
- [ ] Alternate movie versions: accurate resolution/codec/size/language choices, active-version clarity, missing local copy, resume preservation and no accidental restart on inspection.
- [ ] Movie filters: year/decade/genre/folder/unwatched/duration/available technical traits without inventing properties; combinations reversible and visible.
- [ ] Validate independent posters versus stills, wrong metadata association, conflicting artwork sizes, mixed HDR/SDR and missing poster/backdrop/overview.
- [ ] Screenshot all movie layouts and details with long titles, multilingual facts, one/movie-many scenarios, focus, menu open, offline-only, metadata-loading and missing-media states.

### P0. TV shows, seasons and episode player
Keep existing show grouping, season picker, episode stills, continuation and intro/credits machinery intact while giving them stronger UI semantics.

- [ ] TV Browse: season/show cards with counts, unwatched status, new episodes and Continue/Next Up; distinguish show poster, season art and 16:9 episode still.
- [ ] Show page: title, summary, relevant metadata, next-to-watch primary action, current season context and strong show→season→episode hierarchy.
- [ ] Season picker: many seasons, specials/Season 0, nonconsecutive season numbers, partial seasons, duplicate episode numbers, correct restore and keyboard operation.
- [ ] Episode rows: S/E index, title, still, duration, synopsis expand, progress/played state, play/resume, active track and missing-artwork fallbacks.
- [ ] Episode-player context: show title, season/episode labels, current/next/previous episode, episode list panel and subtitle/audio choices.
- [ ] Preserve and polish current skip-intro/credits and next-episode overlays: no subtitle collision, clear cancellation, correct timing, uncertain markers and final-episode behavior.
- [ ] Check play-next across season boundary, specials, missing local episodes, interrupted download, partially played finale and last episode of entire show.
- [ ] Do not treat fixed-file order as authoritative episode order; respect known episode indices and library classification.
- [ ] Where manual watched-state correction adds persistence, create a separate capability entry and verify tests; never change raw file properties unexpectedly.

### P0. Media-adaptive player compositions and layout policy
One player controller and media clock; many *views*, not cloned media sessions. The layout must follow real streams and known classification while honoring explicit user preferences.

- [ ] Define a pure, testable `media context → supported controls → layout policy → view model` resolver; use media streams first, reliable library classification next, sensible direct-file fallback.
- [ ] Preserve one playback engine/queue/audio sink/position/volume/rate across layout changes, fullscreen transitions, side panel changes and secondary windows.
- [ ] Profiles: song/artwork; movie/cinema; TV/episode-aware; audiobook/book/chapter; podcast/episode/notes/transcript; recording/waveform; short-sound/quick play; photo/image; slideshow; generic video/technical; future live stream/capture.
- [ ] Media-specific transport priority: music previous/next/shuffle/repeat, video subtitle/audio/versions, audiobook and podcast skip ±seconds/sleep/chapter, recording timeline precision, image fit/zoom/nav.
- [ ] Provide layouts per media type: music artwork/split lyrics/lyrics focus/visualizer stage/compact; movie cinema/standard/details/technical; TV cinema/episode panel; book focus/chapters/minimal; podcast episode/transcript/notes; recording waveform/transcript/review; picture clean/gallery/details.
- [ ] Implement a layout chooser with well-designed thumbnails, descriptive labels, keyboard support and non-disruptive preview without introducing phantom controls.
- [ ] Persist per-type layout and optional contextual-pane preferences; support session-only override; no unwanted per-file preferences without a migration plan.
- [ ] Specify width/height breakpoints and mutually exclusive panels; current media should always retain enough space to use its essential controls.
- [ ] Switch song→book→podcast→recording→video→photo→live without stale title, controls, visualizer, lyrics, artwork, transcript, chapter list or secondary window.
- [ ] Treat feature availability independently from media category: no lyric tab if no lyrics unless explicit empty-state affordance, no seek for unseekable live, no chapters when absent.
- [ ] Do not reset playback or queue, recreate decoders, seek to zero, change saved speed or steal focus merely because a layout changes.
- [ ] Reopen/restore previous layout and docked panels from safe normalized settings; recover malformed preferences and obsolete enum values.

### P1. Pictures, slideshows, other video and All Videos
Photo/image playback and generic video need practical, file-first controls rather than decorative movie metadata.

- [ ] Pictures: aspect-aware grid, original file ratio, transparency backgrounds, filenames/date/dimensions, lightweight preview and folder groups.
- [ ] Image viewer: fit, actual size, zoom/pan, overview navigator, previous/next, fullscreen, supported orientation, no accidental loss of image offscreen.
- [ ] Slideshow: play/pause/interval, previous/next, end-of-folder rules, prefetch and controls that do not cover the image; no unsupported animated transitions.
- [ ] Test panorama, portrait, tiny image, transparent PNG, animated media where supported, huge dimensions, corrupt/unsupported image and missing drive.
- [ ] Other Videos: landscape thumbnails, duration, original filename, folder, progress, portrait clips and no-frame fallback.
- [ ] All Videos: efficient mixed-type list/grid, deterministic classification/status and rapid discovery for personal footage or tutorials.
- [ ] Preserve existing 'movie/not movie' user classification precedence across rescans and metadata clearing, with clear multi-select/undo expectations.
- [ ] Generic video Technical mode uses reliable track/codec/resolution info only; frame-step/PiP/scrub preview remain gated by existing planned rows.

### P1. Queue, playlists and file operations
Preserve queue identity and all current named-playlist/file import/export behavior.

- [ ] Queue side pane: Now Playing/Up Next, progress, artwork/filename, duration, count, empty state and accessible action hierarchy.
- [ ] Review queue play-next, append, clear, remove, multi-select and reorder; any new persistent drag/keyboard reorder requires correct model, undo and tests.
- [ ] Named playlist page: generated cover/collage, title, count/total time, tracks, Play/Shuffle, Rename/Duplicate/Delete and safe missing-file handling.
- [ ] Mixed video/audio playlists must not mislabel everything as music; preserve individual item type and queue context.
- [ ] Import/export: path portability, supported formats, duplicates, relative paths, inaccessible network shares, empty playlist, recovery and error explanations.
- [ ] Drag/drop across player, library, queue, playlist and folder controls must show exactly which operation will occur; never reclassify media unexpectedly.
- [ ] Disambiguate Add Folder/Rescan/Stop Watching from Delete Actual Files, and confirm destructive operations with explicit scope.

### P0. Edge-case matrix and quality gates
Each item must be a test or an explicit reviewed exclusion, never silently assumed safe.

- [ ] Media: zero or unknown duration, corrupt/unsupported stream, no audio/video track, unusual container, multiple tracks, live/unseekable media, picture-only media, changing resolution and missing codecs.
- [ ] Playback: rapid next/previous/play/stop/seek, early/late seek, rate change, end-of-stream, gapless transition, repeat/shuffle, queue mutation during playback and session teardown.
- [ ] Audio/video overlay: multiple subtitle tracks, caption at bottom, intro prompt, context menu, OSD, timeline markers, visualizer overlay and resize all simultaneously.
- [ ] Performance: time-to-first-useful-paint, playback first picture/sound, scrolling 20k+ items, seek latency, background artwork decode, visualizer 1080p cost, memory and window-thread stalls.

### P0. Architecture, tests and per-PR execution contract
Each future agent should take one unchecked workstream, select bounded tasks and deliver one independently reviewable follow-up PR.

- [ ] Preserve mailbox/threading, seek-generation cancellation, ownership and native-interop boundaries in `AGENTS.md`; no extra runtime dependencies.
- [ ] Add real `Rex.Media.App.Tests` UI Automation and stable `Screenshots.Match` scenes; assert functional behavior separately from pixel comparisons.
- [ ] Required evidence per change: before/after Windows screenshots at normal+compact+accessibility sizes, UIA proof, functional checks, performance deltas, changed settings migration and capability status.
- [ ] Pass when no clipping, wrong focus, stale media art, obscured captions, broken keyboard path, unwanted network/camera activity, hidden core action or measurable unexplained regressions remain.

### P0. Capability matrix, generated parity and documentation protocol
Source of truth is `docs/capability-matrix.json`. Generated files are never hand edited. The Windows-only UI project does **not** grant permission to claim cross-platform completion.

- [ ] Reuse current IDs as appropriate: `UI-01..18`, `LIB-01..12`, `META-09`, `AU-18/19`, `PB-06/08/09/21/26`, `SUB-18..21`, `OSD-01..11`, `VID-03/28`, `WIN-09/10`, `A11Y-01..06`, `I18N-01..05`, `PLF-08`, `NET-15`, `AU-24`, `OUT-06` and `CAP-*`.
- [ ] Proposed **candidate** new IDs, after checking current allocation and overlap: `UI-19` global command launcher; `UI-20` persisted resizable context panels; `UI-21` media-adaptive player layouts; `UI-22` multiple lyric presentation layouts; `UI-23` detached lyrics companion.
- [ ] `verified` needs a passing capability-tagged `[Capability("ID")]` test on that platform; 'we wrote XAML' or a screenshot alone is not sufficient.
- [ ] Respect matrix `preview` parity policy but do not infer that its permissiveness overrides the repository's explicit accepted-gap rule; document decisions and issue references.
- [ ] Use `REXPLAYER_WRITE_DOCS=1` with `./dev.ps1 test` to regenerate `docs/capability-matrix.md` and `docs/parity.md` from JSON, then inspect generated diffs and validation.

### Required screenshot families (expand into stable fixture-backed IDs)
- [ ] Movies: browse/hero/hold/no backdrop, posters/list/collage, in progress, filtered, no results, details/long synopsis/offline, cast/person, versions, missing file
- [ ] TV: browse/show details, season picker with specials, episode still/no still, long synopsis, next-up, last episode, skip intro/credits, missing episode
- [ ] Pictures/video files: image ratios, transparent/corrupt/huge image, zoom/navigator, slideshow, thumbnail-less/portrait video, All Videos mixed
- [ ] Players: movie cinema/details/technical, TV episode panel, music artwork/split/visualizer/compact, audiobook, podcast notes/transcript, recording and image modes

## Standard completion gates · 8 checks per PR
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
- 1/4 · UI foundation and deterministic QA
- **2/4 · Video and adaptive playback (this PR)**
- 3/4 · Music, lyrics and visualizers
- 4/4 · Spoken word, metadata and tools

**Merge gate:** Remain draft until actual implementation, full task evidence, reviewed real screenshots and green current-head CI exist. A planning document is not a completed feature. Original complete source and roadmap remain traceable in [#467](https://github.com/tochi-mba/rexplayer/pull/467).
