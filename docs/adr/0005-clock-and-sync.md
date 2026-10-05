# ADR-0005: Clock and sync

Status: accepted (2026-10-05)

## Context

Audio and video must agree, and only one clock can lead.

## Decision

While audio plays, the audio device's played-sample count is the clock, anchored at the timestamp the current run started at. Without audio, a system clock takes over. Video follows the clock and never drives it.

## Consequences

Sync errors come from one place and are measured by tests that compare what was shown with what was heard.
