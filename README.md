# rexplayer

**A REX Technologies product.** A Windows media player built from scratch: its own engine, its own
audio pipeline, and a command line made for scripts.

[Download](https://github.com/tochi-mba/rexplayer/releases/latest) ·
[Website](https://tochi-mba.github.io/rexplayer/) ·
[Capability matrix](docs/capability-matrix.md) ·
[What's new](CHANGELOG.md)

> **Status: 0.1.** The engine and the `rexplay` command line play WAV, RF64 and AIFF through
> Windows audio. MP3, FLAC, video, the player window and the rest of the
> [capability matrix](docs/capability-matrix.md) arrive release by release.

## Install

1. Download `rexplayer-Setup-<version>.exe` from the
   [latest release](https://github.com/tochi-mba/rexplayer/releases/latest). It installs for you
   only, with no administrator prompt, and puts `rexplay` on your PATH.
2. The installer is not signed yet, so Windows may say "Windows protected your PC". Choose
   **More info**, then **Run anyway**.
3. Prefer no installer? Each release has a portable zip and a `.sha256` file to check it against.

## Everyday use

| Want to… | Do this |
|---|---|
| Play a file | `rexplay play song.wav` |
| Start part-way through | `rexplay play song.wav --start 1:30` |
| Play quietly | `rexplay play song.wav --volume 40` |
| See what a file contains | `rexplay probe song.wav` |
| Capture exactly what would be heard | `rexplay play song.wav --aout wav:capture.wav` |
| Drive it from a script or an agent | add `--json`, or run `rexplay agent capabilities` |

## Command line

Every command prints plain sentences, or with `--json` exactly one JSON document:
`{"ok":true,"protocolVersion":1,"command":"...","data":{...}}`, or `"ok":false` with an `"error"`.
Exit codes are 0 for success and 1 for failure. `rexplay help` lists every command and option.

## How it works

The engine is a set of small .NET projects with a strict dependency graph
([architecture](docs/architecture.md)). Format readers recognise media by its bytes, rexplayer's own
decoders handle the open formats and Windows' licensed decoders handle the patented ones, and the
audio device's clock leads playback. Every line of the engine is covered by tests on each change,
including exact-sample checks of what was played ([testing](docs/testing.md)).

## Security

rexplayer collects nothing and needs no account. See [SECURITY.md](SECURITY.md) for how to report a
problem.

## Licence

MIT. Copyright (c) 2026 REX Technologies. See [LICENSE](LICENSE).
