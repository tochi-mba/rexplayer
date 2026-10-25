# rexplayer on a Mac

rexplayer targets macOS 12 Monterey and later, on Apple Silicon and Intel Macs. Its playback
engine, command semantics and persistence share code with Windows, but **the current Mac window
does not yet provide every Windows control, preference or library view**. The
[capability matrix](capability-matrix.md) and [Mac parity ledger](parity.md) distinguish features
that are implemented, independently verified and still planned.

## Which download

Open the Apple menu, then **About This Mac**.

| It says | Download |
|---|---|
| Chip: Apple M1, M2, M3, M4 or later | `rexplayer-mac-osx-arm64.zip` |
| Processor: Intel | `rexplayer-mac-osx-x64.zip` |

It also shows the macOS version. Below macOS 12, see [an older Mac](#an-older-mac).

## Current Mac feature coverage

The Mac app provides the AppKit playback window, transport and seek controls, volume and
mute, a Metal video surface, native audio output and codecs, Finder open/drop handling, a native
queue and searchable watched-folder library, saved-position bookmarks, native playlist file and
named-playlist controls, recent-media and quick-slot actions, track/subtitle synchronization tools,
a shortcut editor with import/export, a 10-band audio equalizer with stereo and ReplayGain choices,
preferences, selected shared playback commands, and media information. Support for richer library
pages, subtitle rendering and editing, complete preferences, video effects, and many
platform-specific conveniences is still in progress. A capability is not considered
**verified** just because a shared engine has its implementation.

One signed, universal download supports both Intel and Apple Silicon; separate architecture
packages are available too. A Mac package is built with the macOS 26 toolchain, but the final
universal app must also **launch, play a test video and quit on a macOS 15 Sequoia runner** in
CI. That detects crashes the build-host smoke test alone would miss. Passing macOS 15 CI is
not a substitute for checking the originally affected physical Mac running 15.8.1.

## Installing

1. Open the zip (double-click). You get **rexplayer.app**.
2. Drag rexplayer.app into **Applications**.

## Opening it the first time

rexplayer is not signed with an Apple Developer ID yet, so the Mac asks once before it opens it.
After that it opens like any other app.

**macOS 12 Monterey, 13 Ventura and 14 Sonoma**

1. In Applications, hold **Control** and click rexplayer, then choose **Open**.
2. Choose **Open** again in the box that appears.

**macOS 15 Sequoia and later**

1. Double-click rexplayer. The Mac says it cannot check it; choose **Done**.
2. Open **System Settings**, then **Privacy & Security**.
3. Scroll down to the message that rexplayer was blocked, and choose **Open Anyway**.
4. Confirm with your password or Touch ID, then choose **Open**.

If the Mac says the app "is damaged and can't be opened", it was downloaded in a way that marked
it quarantined. Open **Terminal** and run, once:

```sh
xattr -dr com.apple.quarantine /Applications/rexplayer.app
```

## If the app closes immediately

If there is no rexplayer window or log, the process may have stopped before managed startup.
On an ad-hoc-signed .NET app, the JIT entitlement is required even when macOS accepts the
signature. To inspect a downloaded bundle from a trusted release, use:

```sh
codesign --verify --deep --strict /Applications/rexplayer.app
codesign -d --entitlements :- /Applications/rexplayer.app
```

The second command should show `com.apple.security.cs.allow-jit` set to `true`. If it is
missing, use a corrected release rather than trying to change the app signature yourself.
The app is not Developer ID signed or notarised yet, so Gatekeeper still requires the
one-time approval described above. If the entitlement is present but the app still aborts,
the macOS crash report and the exact Mac model, CPU architecture and macOS patch version
are needed to diagnose the remaining failure.

## Where rexplayer keeps its files

Settings, the library, bookmarks, history and logs are in
`~/Library/Application Support/REX/rexplayer` (the same files Windows keeps under
`%LocalAppData%\REX\rexplayer`). **Help, Show the log** opens the log folder.

## An older Mac

rexplayer needs macOS 12 or later, which is what .NET, the platform it is written in, supports.
Many Macs from 2012 to 2015 stop at macOS 10.15 or 11 officially, but can run macOS 12 with
OpenCore Legacy Patcher, a free community tool:

1. Check the Mac's model is on the patcher's
   [supported models list](https://dortania.github.io/OpenCore-Legacy-Patcher/MODELS.html).
2. Back up the Mac with Time Machine first.
3. Follow the patcher's own guide to install macOS 12 Monterey (newer versions are possible on
   many models, but Monterey is the lightest that rexplayer supports).

Macs from before 2012 cannot run macOS 12 at all, so rexplayer cannot run on them.

## Mac parity work in progress

The Mac capability entries marked **built** mean implementation work exists, not that all
edge cases, controls, platform versions and behaviours have been checked. The remaining
**planned** entries have not been completed. In particular, the native tracks panel now
selects audio and primary/secondary subtitle tracks and aligns delay marks; the audio effects
panel uses the shared sound settings. Full platform verification and status promotion to
**verified** are separate tasks after implementation. See the generated [parity ledger](parity.md)
for unresolved differences; do not treat the **gap** count in that document as the number of
outstanding rows.

## Build-first Mac parity progress

Recent work adds a native music stage with cover art, source-timed local lyrics and audio-level
meter; source-resolution PNG frame saving; video aspect, crop, zoom and pan controls; a Dock menu
with recent media and transport actions; a Dock playback badge and sleep-aware pausing; configurable
mouse/trackpad input; and playback-statistics and subtitle overlays. Shared resume, library backup
and missing-media recovery features now have Mac entry points.

A row marked **built** has code but is not fully proved on macOS. Unimplemented capabilities stay
**planned**. The extensive visualizer catalogue and full subtitle typography still need implementation.
Offline transcription, native system media controls, and checksum-verified update downloads
now have Mac implementation paths; full installation and release verification remain separate. The snapshot UI currently
exports PNG; JPEG and BMP controls remain outstanding. See the generated [parity ledger](parity.md).

## Second implementation pass

The native queue now edits start, end and run-time per entry. On every active session the Mac
writes a local crash checkpoint, removes it on clean shutdown and offers a nonmodal resume
prompt after an interruption. The app also holds appropriately scoped macOS process activities
while video or music plays, and drops them when playback stops. The subtitle overlay honours
source-provided per-run word timings and exposes controls for font, colour, opacity, weight,
margin and style policy. The styling work is partial: complete embedded-style, outlined/shadowed
text and kinetic-caption parity are still planned. Source cue top/middle/bottom positions,
forced-bottom placement and letterbox-aware margins now have native Mac implementation.

All newly advanced capabilities are recorded as **built**, not verified. No full Mac parity
verification has been performed as part of this implementation batch.

## Mac system media controls and update security

Control Center, headset commands and media keys are registered through the native MediaPlayer
remote command center. Now Playing publishes the current title, artist, embedded artwork,
position, duration and state, and removes handlers at shutdown.

The software updates window checks the owner's pinned GitHub release feed on the configured cadence
or on demand. Mac universal ZIPs are downloaded with resumable transfers and accepted only after
matching a companion SHA-256 file. The verified archive can be opened for manual installation.
Automatic in-place installation, restart-session handoff, deferral and full updater parity
remain **planned**; neither this UI nor the ledger implies they have been implemented.

## Offline speech and kinetic captions implementation

The macOS project now references the portable Whisper adapter and the shared transcription
worker. The native panel allows an explicit download of a checksum-pinned model, on-demand
transcription of a local file, background transcription of watched library videos, pause
and model-quality preferences, export to SRT/VTT/LRC/JSON, and a progressively filled
"Transcript" subtitle track from the playhead. The worker and cache also keep partial
results and retry state. Speech does not leave this device.

The Mac also draws source-timed kinetic words from the portable CaptionDirector and
accepts the preset, layout, source, and energy commands. This rendering path remains
partial until all movement, face-avoidance, typography, and per-word effects offered
by the capability are implemented; therefore the full OSD-11 row remains planned.
The speech capabilities above are **built**, not verified, until Mac runtime/model
and UI testing is performed.

## Nonblocking playback interactions

Playback commands now accept time and location entry directly in the player rather than opening
blocking alerts. Media information and queue timing use separate, nonmodal AppKit windows;
playlist naming uses an inline editor, and destructive history, bookmark and playlist actions
ask for confirmation inside the main window. Native file-opening, saving, library-backup,
subtitle, shortcut, snapshot, speech-export and diagnostics pickers use completion callbacks
instead of modal event loops. The UI-17 parity row is **built**, not verified, until keyboard,
accessibility and runtime coverage is complete.

Subtitle overlays now follow source top/middle/bottom positions when allowed, honor the
force-bottom choice and calculate margins against the current letterboxed video frame. The
second subtitle stream stays at the top by default to avoid overlapping the primary stream.
OSD-03 is **built**. OSD-01 now also has native attributed text styling for font,
size, color, embedded bold/underline, outline, shadow, and background; complex-script and
right-to-left rendering use AppKit's natural writing direction and font shaping (OSD-09).
These rows are **built**, not verified. Rich embedded subtitle formats, full ASS style policy
and visual verification remain separate work.

## Playback keyboard and decoder policy implementation

F6 and Shift+F6 now cycle the main transport, seek bar and volume regions without
consuming the key in unrelated windows. The Mac transport has an explicit Tab traversal
sequence and accessible labels on the primary interactive controls. Keyboard completeness
(A11Y-02) remains **planned** because not every secondary panel has undergone a keyboard audit.

The Mac playback Preferences window now exposes automatic hardware decoding, a global
hardware-disable option, and separate hardware H.264/HEVC choices for subsequently opened
videos. These choices pass through shared settings, the decoder factory, and VideoToolbox's
documented decoder specification. When disabled for a codec, VideoToolbox is explicitly
requested to decode it in software. Automatic hardware selection is otherwise permitted.
The statistics overlay reports both the *configured per-codec policy* and the decoder's
reported source (hardware, software, or rexplayer). Recovery/fallback reasoning when a
requested hardware decoder is unavailable and complete zero-copy handling remain missing,
so VID-01 stays **planned** rather than being marked complete prematurely.
