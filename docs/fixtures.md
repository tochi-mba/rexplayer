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
