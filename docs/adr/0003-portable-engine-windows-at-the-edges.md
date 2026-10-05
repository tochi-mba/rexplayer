# ADR-0003: Portable engine, Windows at the edges

Status: accepted (2026-10-05)

## Context

Only a few parts need Windows: audio and video output, Media Foundation, interop and the app.

## Decision

Engine projects target `net10.0`; adapters, interop and the executables target `net10.0-windows` (the app a Windows SDK version). `ArchitectureTests` pins every project's framework, and CI runs the pure suite on Linux.

## Consequences

The engine cannot grow a hidden Windows dependency, and parsers can be fuzzed anywhere.
