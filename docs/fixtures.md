# Fixtures

Every media file the tests use is either built in code by the test kit or committed here with a
description of how it was made. No downloaded media is used.

## Built in code

| Builder | Makes |
|---|---|
| `WavBuilder` | WAV files of any format tag, RIFF/RIFX/RF64, extensible formats, LIST/INFO tags, unfinished recordings |
| `AiffBuilder` | AIFF and AIFF-C files with any compression type and text chunks |
| `Pcm.SineWav` | A sine wave as 16-bit WAV, for spectral checks |
| `Pcm.RampWav` | A WAV whose samples count up, so any sample's position can be checked exactly |
| `FlacBuilder` | FLAC streams coded exactly as a test asks: every subframe type and predictor, Rice and escaped residuals, wasted bits, every stereo mode, 4 to 32 bits, 1 to 8 channels, fixed or variable blocks, and every metadata block |
| `Layer3FrameWriter` (in the tests) | Layer III frames written field by field: intensity stereo at every position and table, mixed blocks, reused scalefactors, CRC-protected frames, and each malformed field |
| `Mp4Writer` | ISO base media files box by box: every sample table form (stsz, stz2, co64, signed ctts, stss), version 1 headers, edit lists with pauses, QuickTime sound versions 1 and 2, ipcm, every codec's sample entry, iTunes tags and chapter text tracks, and movie fragments with every header and run flag |
| `Id3Builder` | ID3v2.2, 2.3 and 2.4 tags with every text encoding, unsynchronisation, compressed, encrypted and grouped frames, pictures and chapters |

## Committed

| File | Contents | How it was made |
|---|---|---|
| `tests/fixtures/smoke/tone.wav` | 1 second, 440 Hz sine at a quarter of full scale, 8 kHz, mono, 16-bit | Written sample by sample by a PowerShell loop: `round(0.25 * 32767 * sin(2π · 440 · i / 8000))` |
| `tests/fixtures/flac/stereo-16bit-44k-lpc.flac` | 0.6 s, 440 Hz and 660 Hz tones with a little noise, 44.1 kHz, stereo, 16-bit, high-order LPC | `scripts/make-fixtures.ps1` (FFmpeg, compression level 8) |
| `tests/fixtures/flac/mono-24bit-96k-sweep.flac` | 0.25 s rising sweep, 96 kHz, mono, 24-bit | `scripts/make-fixtures.ps1` (FFmpeg, compression level 12) |
| `tests/fixtures/flac/surround-51-16bit-48k.flac` | 0.2 s, a different tone on each of six channels, 48 kHz, 5.1, 16-bit | `scripts/make-fixtures.ps1` (FFmpeg, compression level 5) |
| `tests/fixtures/flac/stereo-16bit-22k-midside-fixed.flac` | 0.5 s, two phase-shifted 220 Hz tones, 22.05 kHz, stereo, 16-bit, forced mid/side with fixed predictors | `scripts/make-fixtures.ps1` (FFmpeg, compression level 0) |

The FLAC fixtures come from an encoder rexplayer did not write. Their oracle is inside each file: the
STREAMINFO MD5 the encoder computed from its source samples, which a lossless decode must reproduce.

| `tests/fixtures/mp3/stereo-44k-128k-cbr.mp3` | 0.4 s, 440 Hz and 660 Hz with a little noise, MPEG-1, 44.1 kHz, 128 kbit/s, joint stereo | `scripts/make-fixtures.ps1` (FFmpeg with LAME) |
| `tests/fixtures/mp3/stereo-44k-vbr-bursts.mp3` | 0.4 s of sharp bursts that force short blocks, MPEG-1, 44.1 kHz, variable bitrate | `scripts/make-fixtures.ps1` (FFmpeg with LAME, quality 2) |
| `tests/fixtures/mp3/mono-48k-vbr.mp3` | 0.3 s rising sweep, MPEG-1, 48 kHz, mono, variable bitrate | `scripts/make-fixtures.ps1` (FFmpeg with LAME, quality 5) |
| `tests/fixtures/mp3/stereo-22k-64k-mpeg2.mp3` | 0.4 s, two tones, MPEG-2, 22.05 kHz, 64 kbit/s | `scripts/make-fixtures.ps1` (FFmpeg with LAME) |
| `tests/fixtures/mp3/mono-8k-16k-mpeg25.mp3` | 0.5 s, two tones, MPEG 2.5, 8 kHz, 16 kbit/s | `scripts/make-fixtures.ps1` (FFmpeg with LAME) |
| `tests/fixtures/mp3/*.reference.wav` | Each MP3 above decoded by FFmpeg's own decoder to 24-bit PCM, encoder delay and padding already removed | `scripts/make-fixtures.ps1` |

The MP3 fixtures come from an encoder rexplayer did not write, and their references from a decoder
it did not write. rexplayer's decode must match each reference to the conformance standard's "full
accuracy" (RMS difference under 2^-15/sqrt(12), no sample more than 2^-14 apart) and be exactly as
long, which also proves the gapless trim.

| `tests/fixtures/mp4/mp3-in-mp4.mp4` | 0.2 s, two tones, MP3 in MP4 with a gapless edit list | `scripts/make-fixtures.ps1` (FFmpeg with LAME) |
| `tests/fixtures/mp4/flac-in-mp4.mp4` | The same tones, FLAC in MP4 | `scripts/make-fixtures.ps1` (FFmpeg) |
| `tests/fixtures/mp4/pcm-s16le.mov`, `pcm-s24be.mov`, `pcm-f32le.mov` | The same tones as QuickTime PCM in three storage forms | `scripts/make-fixtures.ps1` (FFmpeg) |
| `tests/fixtures/mp4/*.reference.wav` | Each audio file above decoded by FFmpeg to 24-bit PCM, edit lists applied | `scripts/make-fixtures.ps1` |
| `tests/fixtures/mp4/alac.m4a` | The same tones as Apple Lossless, to check a codec rexplayer names but does not yet decode | `scripts/make-fixtures.ps1` (FFmpeg) |
| `tests/fixtures/mp4/h264-aac.mp4` | 0.4 s test pattern, 128x72 at 25 fps, H.264 with B-frames and AAC, tags and two chapters | `scripts/make-fixtures.ps1` (FFmpeg with x264, SEI removed) |
| `tests/fixtures/mp4/h264-aac-fragmented.mp4` | The same as a fragmented MP4 | `scripts/make-fixtures.ps1` |
| `tests/fixtures/mp4/h264-rotated.mov` | 0.2 s of the pattern with a display matrix turning it 90 degrees anticlockwise | `scripts/make-fixtures.ps1` |

The H.264 fixtures have their SEI units removed, because the encoder writes its name and web
address into one; `RepositoryTests` reads binary fixtures for banned names too.
