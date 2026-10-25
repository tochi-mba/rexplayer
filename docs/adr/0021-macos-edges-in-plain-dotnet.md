# ADR-0021: The Mac's edges in plain .NET

Status: accepted (2026-10-21)

## Context

rexplayer is getting a macOS build (ADR-0020). Its engine is portable already; what the Mac
needs is the edges Windows has: audio output, the operating system's decoders, a picture surface,
the window. Microsoft's macOS SDK binds all of Apple's frameworks, but it needs Xcode and a Mac to
build anything that uses it. Most of what the engine needs from macOS is plain C: AudioToolbox,
CoreAudio, VideoToolbox, CoreMedia, CoreVideo and ImageIO.

## Decision

1. Everything below the window reaches macOS through `LibraryImport` from plain `net10.0`
   projects: `Rex.Media.Interop.Mac` holds every native declaration and every pointer, as
   `Rex.Media.Interop` does on Windows, and the adapters (`Rex.Media.Audio.CoreAudio` first) only
   sequence its calls (ADR-0008). They build on any machine with the ordinary SDK.
2. These projects name their framework explicitly, so the coverage gate, which runs on Windows,
   leaves them alone; their proof is the portable suite run on GitHub's macOS runner, where tests
   that need a Mac run for real and skip with their reason anywhere else, and the hardware run on a
   real Mac. Decisions they make live in pure classes on the coverage list (`SampleRing`).
3. Only the window, which needs AppKit's Objective-C classes, will use Microsoft's macOS SDK
   (`net10.0-macos`), built on the macOS runner.
4. `rexplay` on a Mac is `Rex.Media.Cli.Mac`: the same command line in `Rex.Media.AppCore`, with the
   Mac's parts composed in. `./dev.ps1 smoke` holds both builds to one contract.

## Consequences

The Mac's engine edges are built and type-checked on every Windows build and tested on every pull
request's Mac run, with no Xcode in the loop. The Windows interop rule extends to one more project,
and the window remains the only part a Windows machine cannot build.
