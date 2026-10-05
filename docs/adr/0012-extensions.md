# ADR-0012: Extensions

Status: accepted (2026-10-05)

## Context

Users will want features nobody ships, but an extension must never take playback down.

## Decision

Extensions are .NET assemblies loaded into collectible load contexts with declared capabilities; one that throws is unloaded and reported. Local extensions are trusted code; there is no sandbox claim.

## Consequences

Extensibility without pretending to isolation the platform does not give.
