# Parity ledger

Every row of the [capability matrix](capability-matrix.md) where the platforms differ, generated from
`capability-matrix.json`. Parity policy: **preview**. Under *preview* a row may be further along on one
platform while the other catches up; under *locked* every must row is verified on both platforms or marked not
applicable with a reason, and CI blocks a release otherwise (ADR-0020). A **gap** is a difference someone has
accepted for now: its note says why and its issue says who closes it.

Totals: 113 rows differ (87 must), 2 not applicable, 0 gaps.

## Playback core

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| PB-05 | Go to time | must | verified | built |  |
| PB-10 | Bookmarks (per file) | should | verified | built |  |
| PB-11 | Resume where you left off | must | verified | planned |  |
| PB-12 | Repeat & shuffle | must | verified | built |  |
| PB-16 | Snapshot | must | verified | planned |  |
| PB-17 | Track selection | must | built | planned |  |
| PB-19 | Audio delay | must | built | planned |  |
| PB-20 | Subtitle delay & sync | must | built | planned |  |
| PB-23 | Start/stop/run time per item | should | built | planned |  |
| PB-27 | Media key / external control | must | built | planned |  |

## Formats: Containers / demuxers

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| FMT-C09 | Ogg (+OGM Could) | must | built | planned |  |
| FMT-C19 | Image files as media | should | verified | planned |  |

## Formats: Audio decoders

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| FMT-A05 | AAC-LC / HE-AAC v1/v2 (/xHE-AAC if MF supports it) | must | verified | planned |  |
| FMT-A10 | Opus (SILK/CELT/hybrid, multistream for surround) | must | built | planned |  |

## Formats: Subtitles & captions

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| SUB-01 | SubRip (SRT) | must | verified | planned |  |
| SUB-02 | WebVTT | must | built | planned |  |
| SUB-03 | ASS / SSA | must | built | planned |  |
| SUB-04 | MP4 timed text (tx3g / mov_text) | must | built | planned |  |
| SUB-05 | SAMI, MicroDVD (frame-based with fps), SubViewer 1/2, MPL2, VPlayer | should | built | planned |  |
| SUB-14 | Sidecar autoload | must | verified | planned |  |
| SUB-15 | Character encoding | must | built | planned |  |
| SUB-16 | Drop a subtitle file on the video | must | verified | planned |  |
| SUB-18 | Word timing | should | verified | planned |  |
| SUB-19 | Local transcription | should | verified | planned |  |
| SUB-20 | Library transcription | should | verified | planned |  |
| SUB-21 | Progressive transcription from the playhead | should | verified | planned |  |

## Formats: Metadata & artwork

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| META-06 | Folder art discovery | must | verified | planned |  |
| META-09 | Lyrics display | could | verified | planned |  |

## Audio

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| AU-01 | Default-device output | must | verified-hardware | planned |  |
| AU-05 | Volume | must | verified | planned |  |
| AU-06 | Mute | must | verified | built |  |
| AU-07 | 10-band graphic equaliser | must | verified | planned |  |
| AU-11 | Stereo mode | must | verified | planned |  |
| AU-12 | Channel layouts & downmix | must | verified | planned |  |
| AU-13 | ReplayGain / R128 | should | verified | planned |  |
| AU-15 | Pitch-preserving speed | must | verified | planned |  |
| AU-18 | Visualisations | should | verified | planned |  |
| AU-19 | Audio-only presentation | must | verified | planned |  |
| AU-20 | Resampler quality | must | built | planned |  |
| AU-21 | Language preferences | must | verified | planned |  |

## Video & rendering

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| VID-01 | Hardware decoding policy | must | verified | planned |  |
| VID-02 | Frame pacing | must | built | planned |  |
| VID-03 | Fullscreen | must | verified | planned |  |
| VID-04 | Window sizing | must | verified | planned |  |
| VID-05 | Aspect ratio | must | verified | planned |  |
| VID-06 | Crop | must | built | planned |  |
| VID-07 | Zoom & pan | should | verified | planned |  |
| VID-23 | Colour pipeline correctness | must | verified | planned |  |
| VID-25 | Late-frame policy | must | verified | planned |  |
| VID-26 | Title on start | should | verified | planned |  |
| VID-27 | Always on top | must | verified | planned |  |
| VID-29 | Power management | must | built | planned |  |
| VID-31 | Stats overlay | should | verified | planned |  |

## Subtitle & OSD rendering

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| OSD-01 | Text subtitle style | must | verified | planned |  |
| OSD-02 | Subtitle scaling keys | must | built | planned |  |
| OSD-03 | Placement | should | verified | planned |  |
| OSD-04 | Dual subtitles | should | verified | planned |  |
| OSD-05 | ASS style policy | must | built | planned |  |
| OSD-07 | OSD messages | must | verified | planned |  |
| OSD-09 | Complex scripts & RTL | must | built | planned |  |
| OSD-11 | Kinetic captions | should | verified | planned |  |

## Playlist, library & persistence

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| LIB-01 | Playlist pane | must | built | planned |  |
| LIB-02 | Adding media | must | verified | planned |  |
| LIB-03 | Save / load playlists | must | verified | planned |  |
| LIB-04 | Named playlists | must | verified | planned |  |
| LIB-05 | Media library | must | verified | planned |  |
| LIB-07 | Recent media | must | built | planned |  |
| LIB-08 | Playlist quick slots | could | verified | planned |  |
| LIB-09 | Missing files | should | verified | planned |  |
| LIB-10 | Library backup / export | should | verified | planned |  |
| LIB-11 | Crash-safe store | must | verified | built |  |

## UI surface

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| UI-01 | Main window | must | verified | planned |  |
| UI-02 | Menu system | must | built | planned |  |
| UI-03 | Video context menu | must | built | planned |  |
| UI-04 | Fullscreen controller | must | verified | planned |  |
| UI-05 | Minimal interface | must | verified | planned |  |
| UI-07 | Effects & filters panel (Ctrl+E) | must | built | planned |  |
| UI-09 | Log console (Ctrl+M) | must | verified | planned |  |
| UI-10 | Preferences | must | built | planned |  |
| UI-11 | Hotkey editor | must | verified | planned |  |
| UI-13 | Themes | must | built | planned |  |
| UI-14 | First run | must | verified | planned |  |
| UI-15 | What's new | should | verified | planned |  |
| UI-16 | Window title | should | verified | planned |  |
| UI-17 | In-window confirmations | must | built | planned |  |
| UI-18 | Mouse & touch | must/should | built | planned |  |

## Tools, interfaces, extensions & CLI

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| TOOL-01 | `rexplay` CLI (human mode) | must | built | planned |  |
| TOOL-02 | Machine mode | must | verified | planned |  |
| TOOL-03 | Local automation / remote-control interface | must | verified | planned |  |
| TOOL-06 | Single instance | must | verified | planned |  |
| TOOL-09 | Updater | should | verified | planned |  |
| TOOL-10 | Diagnostics bundle | should | verified | planned |  |
| TOOL-11 | System probe | must | verified | planned |  |

## Platform integration

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| WIN-01 | Distribution | must | built | planned |  |
| WIN-03 | Explorer verbs | must | planned | not-applicable | Explorer verbs are Windows' own; on macOS the Finder's Open With comes from the file associations (WIN-02). |
| WIN-04 | AutoPlay handlers | should | planned | not-applicable | AutoPlay has no macOS equivalent. |
| WIN-05 | System Media Transport Controls | must | built | planned |  |
| WIN-06 | Taskbar | must | built | planned |  |
| WIN-07 | Jump list | should | built | planned |  |
| WIN-09 | High DPI | must | built | planned |  |
| WIN-10 | Theme & materials | must | built | planned |  |
| WIN-11 | Power awareness | must | built | planned |  |
| WIN-13 | Minimum OS | must | built | planned |  |
| WIN-14 | Crash recovery | must | verified | planned |  |

## Accessibility & internationalisation

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| A11Y-01 | Named, automatable controls | must | verified | planned |  |
| A11Y-02 | Keyboard completeness | must | built | planned |  |
| I18N-05 | Culture handling | must | built | planned |  |

## Privacy, security & diagnostics

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| PRIV-01 | No telemetry | must | built | planned |  |
| PRIV-02 | Network policy | must | built | planned |  |
| PRIV-03 | History controls | must | verified | planned |  |
| SEC-01 | Hostile-input hardening | must | built | planned |  |
| SEC-05 | Update integrity | must | verified | planned |  |
| SYS-01 | Logging | must | built | planned |  |
