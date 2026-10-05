# ADR-0015: The name rule and the capability matrix

Status: accepted (2026-10-05)

## Context

The project describes itself only in its own terms.

## Decision

The repository never names other media players or their organisations. The feature list is `docs/capability-matrix.json`; a repository test checks every tracked file and CI checks commit and pull-request text. Matrix rows are marked verified only while a `[Capability]` test proves them.

## Consequences

Claims about what rexplayer does are always backed by a passing test.
