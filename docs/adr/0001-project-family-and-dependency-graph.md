# ADR-0001: Project family and dependency graph

Status: accepted (2026-10-05)

## Context

The engine has many concerns (formats, codecs, audio, video, network, discs) that must stay separable for a decade of maintenance.

## Decision

The product is a family of `Rex.Media.*` projects. Each project may reference only the projects its row in `ArchitectureTests` allows, and the test fails on any other reference. The executables take product names: `Rex.Media.Cli` builds `rexplay.exe` and `Rex.Media.App` builds `rexplayer.exe`.

## Consequences

Adding a dependency edge is a reviewed change to the test, never an accident. Projects are added when they get code, not as empty stubs.

## Amendments

- 2026-10-05: `Containers` may reference `Codecs`. A demuxer for a raw stream (native FLAC, MP3, and later ADTS, AC-3 and Annex B video) has no container framing of its own; it finds frames by parsing the codec's own headers. Those parsers live once, in `Codecs`, and both the demuxer and the decoder use them. `Codecs` references nothing above `Primitives` and `Diagnostics`, so the graph stays acyclic.
