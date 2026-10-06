# Changelog

Every version of rexplayer that reaches users, newest first. A version is published by the CI
pipeline the moment its number reaches `main`.

## 0.2.0 - 2026-10-06

### Added

- **MP3.** rexplayer's own Layer III decoder for MPEG-1, MPEG-2 and MPEG 2.5 at every rate, matching
  an independent decoder to the conformance standard's full accuracy. Joint, intensity and
  mixed-block streams, free format, and Xing, LAME and VBRI tags for exact durations.
- **FLAC.** rexplayer's own decoder for all of RFC 9639, from 4 to 32 bits and one to eight
  channels, with seek tables, cue-sheet chapters and embedded pictures.
- **Gapless playback.** `rexplay play` takes several files and plays them into one output with
  nothing between them, and MP3 encoder delay and padding are cut to the sample.
- **Tags.** ID3v2.2 to 2.4, ID3v1, Vorbis comments and cover art, read wherever they appear.

### Fixed

- A sound card that failed part-way through a packet could make the engine release an audio
  buffer twice, which could corrupt another stream's audio.

## 0.1.0 - 2026-10-05

### Added

- **The engine.** A media session with a single-threaded command mailbox, a ten-state life cycle,
  sample-accurate seeking that interrupts whatever the pipeline was doing, pause, resume and stop.
- **WAV, RF64 and AIFF.** Every PCM storage format, big- and little-endian, plus G.711 A-law and
  µ-law, with tags read from LIST/INFO and AIFF text chunks.
- **Windows audio.** Playback to the default device through WASAPI, with rexplayer's own resampler
  and channel mixer converting to whatever the device mixes at.
- **The command line.** `rexplay play`, `probe`, `version`, `help` and `agent capabilities`, with
  one-line JSON output for scripts and agents.
