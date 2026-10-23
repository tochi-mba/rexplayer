# Windows metadata and specialist tools workstream

This document preserves the metadata, maintenance, specialist playback tools, preferences, onboarding, update, and Windows-integration scope split from pull request 473. It is a planning and handoff artifact. A checked item requires implementation plus current-head evidence; creating this document does not complete any product work.

## Metadata, artwork, downloads, and maintenance

- [ ] Add media-specific facts editing with clear provenance for embedded data, folder artwork, local overrides, optional enrichment, and generated fallbacks.
- [ ] Support safe single and multi-item detail editing, mixed values, validation, deferred writes for playing files, and progress/error feedback.
- [ ] Add artwork selection, preview, override, reset, resolution/aspect feedback, and explicit source-write consent.
- [ ] Keep indexing and refresh non-blocking while preserving the current route and selected item.
- [ ] Represent growing or incomplete files honestly without fabricated percentages and explain playback limitations.
- [ ] Design library backup and restore with explicit scope, destination, confirmation, transactions, and recovery from errors.
- [ ] Recover moved, renamed, disconnected, inaccessible, and stale media references without deleting listening history.
- [ ] Keep duplicate detection gated by the existing capability contract and require engine evidence before claiming completion.

## Timelines, subtitles, transcription, and media tools

- [ ] Refine the common timeline with readable time, precise seeking, hover feedback, buffered or transcript rails, markers, and keyboard operation.
- [ ] Verify full-screen reveal behavior, multi-monitor placement, taskbar and z-order behavior, subtitle safe areas, and input methods.
- [ ] Prevent marker, caption, tooltip, and volume-flyout collisions while preserving marker precision.
- [ ] Preserve primary and secondary subtitle selection, delay, language, styling, and capability-sensitive controls.
- [ ] Build realistic subtitle appearance previews for bright and dark media, dual tracks, placement, outline, and high contrast.
- [ ] Preserve kinetic-caption choreography, optional normal subtitles, cue provenance, picture avoidance, and cancellation.
- [ ] Design transcript editing around original versus edited text, timing, partial coverage, model selection, search, save/cancel, and export.
- [ ] Model transcription jobs with queued, running, paused, complete, error, download, cancel, and disk-space states.
- [ ] Complete equalizer keyboard support, preamp and zero-line presentation, live preview, presets, and audio-processing preservation.
- [ ] Add honest picture-effects and subject-lock state, preview language, status, errors, focus, responsive layout, and caption-safe placement.
- [ ] Keep technical statistics, logs, media information, diagnostics, copy, and export readable and privacy-aware.
- [ ] Do not expose future playback, video, network, capture, or output controls as operational until their dependencies are implemented and verified.

## Preferences, onboarding, updates, and Windows integration

- [ ] Audit all preference sections for accurate controls, descriptions, units, grouping, focus, validation, and supported behavior.
- [ ] Preserve Save/Cancel, section scroll, responsive navigation, picker behavior, and existing AutomationIDs.
- [ ] Implement advanced preview and grouping work in bounded changes with dedicated evidence.
- [ ] Complete hotkey recording, conflict handling, search, reset, keyboard focus, and app/global distinctions.
- [ ] Keep first run focused on opening files and adding folders, explain optional network/model behavior, and require no account.
- [ ] Improve What's New and About with version, release details, diagnostics, copyable links, accessible text, and coherent branding.
- [ ] Model updater available, downloading, paused, retry, error, restart, offline, and metered-network states without interrupting playback.
- [ ] Unify dialogs, flyouts, confirmations, validation, Escape/cancel behavior, focus restoration, and destructive choices.
- [ ] Preserve native title-bar, Snap, file-association, taskbar, SMTC, Jump List, DPI, theme-material, and minimal-window behavior.

## Required evidence

- [ ] Add focused pure and Windows UI tests for every implemented decision.
- [ ] Capture deterministic screenshots for each changed surface and edge state.
- [ ] Run pure, coverage, Windows build, UI automation, accessibility, screenshot, performance, and warnings-as-errors gates on the final head.
- [ ] Record deferred capability rows explicitly and leave unsupported UI hidden or disabled with an honest explanation.

## Relationship

This is successor 3 of 3 for the oversized Windows spoken-word and tools roadmap in pull request 473. Audiobook implementation moved to the audiobook successor; podcasts and recordings moved to the spoken-word successor. Coordinate shared Settings, player-overlay, and library files before merging.
