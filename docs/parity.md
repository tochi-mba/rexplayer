# Parity ledger

Every row of the [capability matrix](capability-matrix.md) where the platforms differ, generated from
`capability-matrix.json`. Parity policy: **preview**. Under *preview* a row may be further along on one
platform while the other catches up; under *locked* every must row is verified on both platforms or marked not
applicable with a reason, and CI blocks a release otherwise (ADR-0020). A **gap** is a difference someone has
accepted for now: its note says why and its issue says who closes it.

Totals: 51 rows differ (31 must), 2 not applicable, 0 gaps.

## Playback core

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| PB-05 | Go to time | must | verified | built |  |
| PB-10 | Bookmarks (per file) | should | verified | built |  |
| PB-11 | Resume where you left off | must | verified | built |  |
| PB-16 | Snapshot | must | verified | built |  |

## Formats: Audio decoders

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| FMT-A10 | Opus (SILK/CELT/hybrid, multistream for surround) | must | built | planned |  |

## Formats: Subtitles & captions

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| SUB-18 | Word timing | should | verified | built |  |
| SUB-19 | Local transcription | should | verified | planned |  |
| SUB-20 | Library transcription | should | verified | planned |  |
| SUB-21 | Progressive transcription from the playhead | should | verified | planned |  |

## Formats: Metadata & artwork

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| META-09 | Lyrics display | could | verified | built |  |

## Audio

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| AU-01 | Default-device output | must | verified-hardware | built |  |
| AU-06 | Mute | must | verified | built |  |
| AU-07 | 10-band graphic equaliser | must | verified | built |  |
| AU-11 | Stereo mode | must | verified | built |  |
| AU-13 | ReplayGain / R128 | should | verified | built |  |
| AU-15 | Pitch-preserving speed | must | verified | planned |  |
| AU-18 | Visualisations | should | verified | planned |  |
| AU-19 | Audio-only presentation | must | verified | built |  |

## Video & rendering

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| VID-01 | Hardware decoding policy | must | verified | planned |  |
| VID-04 | Window sizing | must | verified | built |  |
| VID-05 | Aspect ratio | must | verified | built |  |
| VID-07 | Zoom & pan | should | verified | built |  |
| VID-26 | Title on start | should | verified | built |  |
| VID-31 | Stats overlay | should | verified | built |  |

## Subtitle & OSD rendering

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| OSD-01 | Text subtitle style | must | verified | planned |  |
| OSD-03 | Placement | should | verified | planned |  |
| OSD-04 | Dual subtitles | should | verified | built |  |
| OSD-05 | ASS style policy | must | built | planned |  |
| OSD-07 | OSD messages | must | verified | built |  |
| OSD-09 | Complex scripts & RTL | must | built | planned |  |
| OSD-11 | Kinetic captions | should | verified | planned |  |

## Playlist, library & persistence

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| LIB-02 | Adding media | must | verified | built |  |
| LIB-03 | Save / load playlists | must | verified | built |  |
| LIB-04 | Named playlists | must | verified | built |  |
| LIB-05 | Media library | must | verified | built |  |
| LIB-08 | Playlist quick slots | could | verified | built |  |
| LIB-09 | Missing files | should | verified | built |  |
| LIB-10 | Library backup / export | should | verified | built |  |

## UI surface

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| UI-01 | Main window | must | verified | built |  |
| UI-04 | Fullscreen controller | must | verified | built |  |
| UI-11 | Hotkey editor | must | verified | built |  |
| UI-14 | First run | must | verified | built |  |
| UI-15 | What's new | should | verified | built |  |
| UI-17 | In-window confirmations | must | built | planned |  |

## Tools, interfaces, extensions & CLI

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| TOOL-09 | Updater | should | verified | planned |  |

## Platform integration

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| WIN-03 | Explorer verbs | must | planned | not-applicable | Explorer verbs are Windows' own; on macOS the Finder's Open With comes from the file associations (WIN-02). |
| WIN-04 | AutoPlay handlers | should | planned | not-applicable | AutoPlay has no macOS equivalent. |
| WIN-14 | Crash recovery | must | verified | built |  |

## Accessibility & internationalisation

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| A11Y-01 | Named, automatable controls | must | verified | planned |  |
| A11Y-02 | Keyboard completeness | must | built | planned |  |

## Privacy, security & diagnostics

| ID | Capability | Priority | Windows | macOS | Note |
|---|---|---|---|---|---|
| SEC-05 | Update integrity | must | verified | built |  |
