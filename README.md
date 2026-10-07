# rexplayer

**A REX Technologies product.** A Windows media player built from scratch: its own engine, a
window to watch and listen in, and a command line made for scripts.

[Download](https://github.com/tochi-mba/rexplayer/releases/latest) ·
[Website](https://tochi-mba.github.io/rexplayer/) ·
[Capability matrix](docs/capability-matrix.md) ·
[What's new](CHANGELOG.md)

> **Status: 0.4.** rexplayer opens in a window of its own: MP4, MOV, MKV and WebM video decoded on
> the graphics card, and MP3, FLAC, WAV, RF64 and AIFF without a gap from one file to the next,
> with a playlist, full screen, snapshots, preferences and one window for everything you open.
> rexplayer decodes MP3, FLAC and PCM itself and hands H.264, HEVC and AAC to Windows' own
> decoders. Subtitles, more formats, streaming and the rest of the
> [capability matrix](docs/capability-matrix.md) arrive release by release.

## Install

1. Download `rexplayer-Setup-<version>.exe` from the
   [latest release](https://github.com/tochi-mba/rexplayer/releases/latest). It installs for you
   only, with no administrator prompt, adds rexplayer to the Start menu and puts `rexplay` on
   your PATH.
2. The installer is not signed yet, so Windows may say "Windows protected your PC". Choose
   **More info**, then **Run anyway**.
3. Prefer no installer? Each release has a portable zip and a `.sha256` file to check it against.

## Everyday use

| Want to… | Do this |
|---|---|
| Play something | Drop it on the window, or press Ctrl+O (a folder: Ctrl+F) |
| Add to what is playing | Hold Ctrl while you drop, or open it while rexplayer is running |
| Pause, jump, change the volume | Space; Left and Right jump 10 seconds; Up and Down change the volume |
| Full screen | Double-click the picture, or press F; Escape leaves |
| Keep a picture | Shift+S saves it to Pictures\rexplayer |
| See every shortcut | Help, Keyboard shortcuts (Ctrl+/) |
| Change how it behaves | View, Preferences (Ctrl+P) |
| Report a problem | Help, Save diagnostics, and attach the zip |
| Play a file from a terminal | `rexplay play song.mp3` |
| Watch a video | `rexplay play film.mkv --window` (Escape or closing the window stops it) |
| Play an album without gaps | `rexplay play 01.flac 02.flac 03.flac` |
| Start part-way through | `rexplay play song.wav --start 1:30` |
| Play quietly | `rexplay play song.wav --volume 40` |
| See what a file contains | `rexplay probe song.wav` |
| Save a picture from a video | `rexplay snapshot film.mkv --at 1:30` (beside the video, as `film.png`) |
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
