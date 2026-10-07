# Capability matrix

Everything rexplayer does or will do, generated from `capability-matrix.json`. A row is **verified** only while a test
marked with its id passes; **built** means the code exists but the row is not fully proved yet.

Totals: 183 planned, 38 built, 68 verified, 1 verified-hardware, 27 post-1.0.

## Playback core

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| PB-01 | Open & play any local file | Content sniffing (never trust extensions), probe order by score, unknown-format message names the bytes it saw | M1–M3 | must | verified |
| PB-02 | Play / pause / stop / toggle | Stop returns to start and releases devices; pause holds the decoders warm | M2 | must | verified |
| PB-03 | Precise seek (time) | Sample-accurate audio, frame-accurate video; seek bar click, drag (keyframe while dragging, precise on release) | M2/M3 | must | verified |
| PB-04 | Relative jumps | Very short 3 s, short 10 s, medium 60 s, long 300 s; every size configurable | M4 | must | verified |
| PB-05 | Go to time | Dialog (Ctrl+T) accepting `h:mm:ss`, `mm:ss` or seconds; also `rexplay seek` | M4 | must | verified |
| PB-06 | Playback rate | 0.25×–4×; coarse steps (0.25/0.5/0.75/1/1.25/1.5/2/3/4) and fine steps of ±0.1; reset to 1.0; pitch preserved by default | M5 | must | verified |
| PB-07 | Frame step | Forward E; backward Shift+E (decode-from-keyframe cache) | M11 | must/should | planned |
| PB-08 | A-B loop | Set A, set B, clear; loop survives a pause; snaps to frames | M11 | must | planned |
| PB-09 | Titles & chapters | Next/previous chapter and title; chapter menu; chapter marks on the seek bar | M3/M11 | must | planned |
| PB-10 | Bookmarks (per file) | Named positions with add, rename, delete and jump; stored in RexStore | M6 | should | planned |
| PB-11 | Resume where you left off | Ask / always / never; a 10 s threshold at either end; per-file resume points (hashed keys); the prompt is an in-window banner, never a modal | M6 | must | planned |
| PB-12 | Repeat & shuffle | Repeat off / one / all; shuffle with no repeats until the list is exhausted | M4 | must | verified |
| PB-13 | Stop after current / pause after current | One-shot toggles | M6 | should | planned |
| PB-14 | Gapless playback | Sample-exact splice for same-format items; bridge or 50 ms crossfade otherwise | M2 | must | verified |
| PB-15 | Crossfade between tracks | 0–12 s setting (default off) | M6 | could | planned |
| PB-16 | Snapshot | Current frame at source resolution, without the OSD; PNG/JPEG/BMP; folder, filename pattern and sequential numbering configurable | M3/M11 | must | verified |
| PB-17 | Track selection | Video / audio / subtitle / secondary subtitle; preferred-language lists; "off" for each | M3–M5 | must | built |
| PB-18 | Program selection (TS) | Next/previous program (service id) for multi-program transport streams | M7 | should | planned |
| PB-19 | Audio delay | ±50 ms steps (J/K), reset, exact entry; range ±10 s; optionally remembered per file | M5 | must | built |
| PB-20 | Subtitle delay & sync | ±50 ms steps (G/H); speed/FPS factor; three-key sync-by-bookmark (mark audio, mark subtitle, apply); reset | M5 | must | built |
| PB-21 | Sleep timer | Stop or pause after N minutes or at the end of the current item, with a fade-out | M6 | should | planned |
| PB-22 | Play-and-exit / play-and-stop / play-and-pause | CLI and settings | M4 | must | verified |
| PB-23 | Start/stop/run time per item | `--start`, `--stop`, `--run-time`; per-item options in the playlist | M4 | should | built |
| PB-24 | Corrupt-file resilience | Skip bad access units, keep playing other streams, rebuild a missing MP4/AVI index by scanning (with progress) | M3/M7 | must | planned |
| PB-25 | Instant start | Cold start under 300 ms to UI; open to first frame under 500 ms on the hardware lane | M15 | must | planned |
| PB-26 | Scrubbing preview | Seek-bar thumbnail preview generated off-thread from keyframes and cached | M11 | should | planned |
| PB-27 | Media key / external control | SMTC, keyboard media keys, Bluetooth headset buttons (§6.13) | M15 | must | planned |

## Formats: Containers / demuxers

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| FMT-C01 | WAV / RF64 / BW64 / Wave64 | WAVE_FORMAT_EXTENSIBLE, channel masks, `LIST/INFO`, `bext`, cue chunks | M1 | must | verified |
| FMT-C02 | AIFF / AIFF-C | Big-endian PCM, `sowt`, float, markers | M1 | must | verified |
| FMT-C03 | MPEG audio elementary (MP1/2/3) | Xing/Info/VBRI headers for duration and seek, LAME gapless delay/padding, ID3v1/v2, APEv2 at the tail | M2 | must | verified |
| FMT-C04 | FLAC native | STREAMINFO, SEEKTABLE, PICTURE, VORBIS_COMMENT, CUESHEET; frame-scan seek when there is no seektable | M2 | must | verified |
| FMT-C05 | MP4 / MOV / 3GP / M4A / M4V / F4V | Progressive + fragmented (moof/mfra/sidx), edit lists (gapless offset), `chap` chapters, `tx3g` text, multiple tracks, `ctts` B-frames, 64-bit boxes, fast-start and non-fast-start | M3 | must | verified |
| FMT-C06 | Matroska / WebM | EBML lacing (Xiph/EBML/fixed), Cues, Chapters (incl. nested/ordered: ordered only as linear in 1.0), Tags, Attachments (fonts for ASS), BlockAdditions, CodecPrivate, live/unknown-size clusters | M3 | must | verified |
| FMT-C07 | MPEG-TS / M2TS (BDAV) | PAT/PMT/SDT/EIT, PCR clock recovery, multi-program, discontinuity handling, 188/192/204-byte packets, PES reassembly | M7 | must | planned |
| FMT-C08 | MPEG-PS / VOB / MPG | Pack/system headers, private streams (AC-3/DTS/LPCM/SPU), SCR | M7 | must | planned |
| FMT-C09 | Ogg (+OGM Could) | Vorbis, Opus, FLAC-in-Ogg, Theora, Speex; chained streams (internet radio); granule-position seek bisection | M7 | must | planned |
| FMT-C10 | AVI / OpenDML | idx1 + indx super-index, broken-index rebuild, VBR MP3, DV type-1/2 | M7 | must | planned |
| FMT-C11 | ASF / WMV / WMA | Via MF's in-box ASF media source as a demux adapter (spec-licence checkpoint, §8.2); DRM-protected files refused with a message | M7 | should | planned |
| FMT-C12 | FLV | AMF0 metadata, H.264/AAC/MP3 tags (VP6/Sorenson payloads: Won't) | M7 | should | planned |
| FMT-C13 | Raw elementary streams | AAC ADTS/LATM, AC-3/E-AC-3, DTS, H.264/HEVC Annex B, MPEG-1/2 video ES, Y4M, raw PCM with user-set format | M3/M7 | must | planned |
| FMT-C14 | CAF | Core Audio Format (ALAC/PCM/AAC payloads) | M7 | could | planned |
| FMT-C15 | AU / SND | Sun/NeXT audio | M7 | could | planned |
| FMT-C16 | WavPack, TTA, Musepack, APE native | With their codecs (§6.2.3) | post | could/wont | post-1.0 |
| FMT-C17 | MXF (OP1a) | Broadcast exchange | post | could | post-1.0 |
| FMT-C18 | DV raw | DIF stream | post | could | post-1.0 |
| FMT-C19 | Image files as media | JPEG/PNG/BMP/GIF/TIFF/WebP/HEIC (via WIC, so OS codecs); a display duration for slideshows in playlists | M6 | should | planned |
| FMT-C20 | Real/NSV/NUT/PVA/SMF-as-container | Legacy | — | wont | post-1.0 |

## Formats: Video decoders

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| FMT-V01 | H.264/AVC (all common profiles incl. High 10 where MF supports it) | MF (D3D11VA → SW MFT) | M3 | must | verified |
| FMT-V02 | HEVC/H.265 (Main, Main 10) | MF + Store extension | M3 | must | verified |
| FMT-V03 | AV1 | MF + AV1 extension; own post-1.0 | M3 | must | planned |
| FMT-V04 | VP9 (profiles 0/2) | MF extension; own M12 | M3/M12 | must | planned |
| FMT-V05 | VP8 | Own (MF if present) | M12 | should | planned |
| FMT-V06 | MPEG-2 video (MP@HL, 4:2:0; 4:2:2 Could) | Own (field/frame pictures, 3:2 pulldown flags) | M7 | must | planned |
| FMT-V07 | MPEG-1 video | Own | M7 | must | planned |
| FMT-V08 | MPEG-4 Part 2 ASP | MF; own post-1.0 | M3 | must | planned |
| FMT-V09 | VC-1 / WMV1/2/3 | MF | M3 | should | planned |
| FMT-V10 | H.263 / H.263+ | MF | M3 | could | planned |
| FMT-V11 | MJPEG (A/B) | Own baseline JPEG | M7 | should | planned |
| FMT-V12 | Uncompressed (I420/NV12/UYVY/YUY2/v210/RGB/BGRA) | Own | M7 | must | planned |
| FMT-V13 | Theora | MF if present; own Could | M12 | could | planned |
| FMT-V14 | DV (25/50) | MF | M7 | could | planned |
| FMT-V15 | VVC | MF only if an extension exists | post | could | post-1.0 |
| FMT-V16 | Legacy (Cinepak, Indeo, Sorenson, RealVideo, VP3/5/6, ProRes, FFV1/HuffYUV) | — (FFV1/HuffYUV own Could post-1.0) | — | wont | post-1.0 |

## Formats: Audio decoders

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| FMT-A01 | PCM (u8/s16/s24/s32/f32/f64, LE/BE, A-law/µ-law) | Own | M1 | must | verified |
| FMT-A02 | MP3 (incl. MPEG-2.5, free-format) | Own | M2 | must | verified |
| FMT-A03 | MP1 / MP2 | Own | M7 | must | planned |
| FMT-A04 | FLAC (incl. 24/32-bit, up to 8 ch) | Own | M2 | must | verified |
| FMT-A05 | AAC-LC / HE-AAC v1/v2 (/xHE-AAC if MF supports it) | MF | M3 | must | verified |
| FMT-A06 | AC-3 | Own (dialnorm, DRC modes, downmix coefficients) | M7 | must | planned |
| FMT-A07 | E-AC-3 | MF ext → own after legal checkpoint | M11/M12 | should | planned |
| FMT-A08 | DTS core (+ core extraction from DTS-HD) | Own | M12 | should | planned |
| FMT-A09 | Vorbis | Own | M7 | must | planned |
| FMT-A10 | Opus (SILK/CELT/hybrid, multistream for surround) | Own | M7 | must | planned |
| FMT-A11 | ALAC | Own | M7 | should | planned |
| FMT-A12 | WMA 1/2/Pro/Lossless | MF | M3 | should | planned |
| FMT-A13 | AMR-NB/WB | MF | M7 | could | planned |
| FMT-A14 | ADPCM variants (IMA, MS, ...) | Own | M7 | should | planned |
| FMT-A15 | LPCM (DVD/BD/AES3) | Own | M7/M13 | must | planned |
| FMT-A16 | TrueHD/MLP, DTS-HD | Passthrough only at 1.0 (§6.3) | M11 | should | planned |
| FMT-A17 | Speex, WavPack, TTA, Musepack, APE | Own Could | post | could | post-1.0 |
| FMT-A18 | MIDI (SMF) | Own small synthesiser (sine/wavetable GM-lite) or the OS synth via midiOut | post | could | post-1.0 |
| FMT-A19 | Tracker modules (MOD/S3M/XM/IT) | Own Could | post | could | post-1.0 |
| FMT-A20 | G.711/G.722 (RTP telephony) | Own G.711; G.722 Could | M12 | could | planned |
| FMT-A21 | ATRAC, QDM2, MACE, RealAudio | — | — | wont | post-1.0 |

## Formats: Subtitles & captions

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| SUB-01 | SubRip (SRT) | Basic tags `<b><i><u><font color>`; tolerant timestamps | M5 | must | verified |
| SUB-02 | WebVTT | Cue settings (line/position/align/size), regions basic, `::cue` styling basic, voice spans | M5 | must | built |
| SUB-03 | ASS / SSA | v1 subset (§7.6), embedded fonts from MKV attachments, collisions, PlayRes scaling | M5 | must | built |
| SUB-04 | MP4 timed text (tx3g / mov_text) | Styles, karaoke Could | M5 | must | built |
| SUB-05 | SAMI, MicroDVD (frame-based with fps), SubViewer 1/2, MPL2, VPlayer | Text formats | M5 | should | built |
| SUB-06 | VobSub (idx/sub) & DVD SPU in VOB | Palette, forced subtitles flag | M7/M13 | must | planned |
| SUB-07 | PGS (HDMV .sup / in M2TS/MKV) | Composition, cropping, forced | M7 | must | planned |
| SUB-08 | DVB subtitles | Region/CLUT/object segments | M7 | should | planned |
| SUB-09 | CEA-608 (in H.264 SEI / MPEG-2 user data) | Roll-up / pop-on / paint-on | M15 | should | planned |
| SUB-10 | CEA-708 | — | post | could | post-1.0 |
| SUB-11 | TTML / DFXP / SMPTE-TT (text) | — | M15 | could | planned |
| SUB-12 | Teletext subtitles (DVB) | — | post | could | post-1.0 |
| SUB-13 | JACOsub, PJS, MPSub, USF, SCC, RealText, Kate | — | post | could/wont | post-1.0 |
| SUB-14 | Sidecar autoload | Same folder plus `Subs/`, `subs/`, `Subtitles/`, `subtitles/`; match levels exact / starts-with / contains / any; language codes in the filename (`movie.en.srt`, `movie.fr.forced.srt`) | M5 | must | verified |
| SUB-15 | Character encoding | BOM/UTF-8 detection, heuristic detector, fallback setting (system ANSI default) with an explicit list (Windows-125x, ISO-8859-x, Shift-JIS, EUC-KR, GB18030, Big5, KOI8-R/U, UTF-16) | M5 | must | built |
| SUB-16 | Drop a subtitle file on the video | Loads it as a subtitle track and selects it | M5 | must | verified |
| SUB-17 | Online subtitle search | Extension point only (§6.12); no service hard-wired in core | M14 | could | planned |

## Formats: Playlists

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| PLF-01 | M3U / M3U8 (extended, `#EXTINF`, relative and absolute paths, UTF-8/ANSI) | R/W | M6 | must | planned |
| PLF-02 | PLS | R/W | M6 | must | planned |
| PLF-03 | XSPF (incl. per-track extension data) | R/W | M6 | must | planned |
| PLF-04 | CUE sheets (single-file albums as virtual tracks, multi-file cues) | R | M6 | must | planned |
| PLF-05 | ASX / WAX / WVX | R | M6 | should | planned |
| PLF-06 | WPL / ZPL | R | M6 | could | planned |
| PLF-07 | B4S, QTL, iTunes XML, SMIL | R | post | could/wont | post-1.0 |
| PLF-08 | Podcast RSS/Atom feeds | R | M8 | should | planned |
| PLF-09 | Directory / recursive folder as playlist | Natural sort (house `NaturalOrder` pattern), ignore list (thumbs, `.nfo`, ...) | M4 | must | verified |

## Formats: Metadata & artwork

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| META-01 | ID3v1/1.1, ID3v2.2/2.3/2.4 | Text frames, APIC, USLT lyrics, CHAP/CTOC chapters, TXXX ReplayGain, unsynchronisation, compression | M2 | must | verified |
| META-02 | Vorbis comments / FLAC blocks / METADATA_BLOCK_PICTURE |  | M2 | must | verified |
| META-03 | MP4 `ilst` atoms (incl. `covr`, `----` freeform) |  | M3 | must | verified |
| META-04 | Matroska tags + attachments |  | M3 | must | verified |
| META-05 | APEv2, ASF attributes, RIFF INFO, AIFF chunks |  | M7 | should | planned |
| META-06 | Folder art discovery | `cover`, `folder`, `front`, `albumart*` × `.jpg`/`.png`/`.webp`, case-insensitive; embedded art preferred | M6 | must | planned |
| META-07 | Tag editing | Write title/artist/album/genre/track/date/comment/artwork for ID3v2.4, Vorbis comments/FLAC and MP4 atoms, via a safe rewrite (atomic replace, padding reuse) | M6 | should | planned |
| META-08 | Online metadata/art lookup | Opt-in (privacy prompt); provider extension point; none hard-wired | M14 | could | planned |
| META-09 | Lyrics display | From USLT / sidecar `.lrc` (synced lines highlighted) | M6 | could | planned |

## Audio

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| AU-01 | Default-device output | WASAPI shared, event-driven; follows the Windows default device unless pinned | M1 | must | verified-hardware |
| AU-02 | Device picker | Menu + cycle hotkey (Shift+A); remembers the pinned device by endpoint id; falls back to default with a notice if it disappears | M11 | must | planned |
| AU-03 | Exclusive mode (bit-perfect) | Opt-in; format negotiation ladder; volume becomes digital-in-graph or locked (setting) | M11 | should | planned |
| AU-04 | Bitstream passthrough | AC-3, E-AC-3, DTS, DTS-HD, TrueHD as IEC 61937 over HDMI/S/PDIF; per-format toggles; DSP bypass indicated | M11 | should | planned |
| AU-05 | Volume | 0–200% (slider tops at 125% by default, configurable to 200%); 5% steps (configurable); wheel over the video; soft-clip above 100%; remembered across sessions (setting); OSD feedback | M4 | must | verified |
| AU-06 | Mute | Digital silence; unmute restores the level | M4 | must | verified |
| AU-07 | 10-band graphic equaliser | ISO octave bands 31/62/125/250/500/1k/2k/4k/8k/16k Hz, ±20 dB per band, preamp ±20 dB; 18 built-in presets (Flat, Classical, Club, Dance, Full bass, Full bass & treble, Full treble, Headphones, Large hall, Live, Party, Pop, Reggae, Rock, Ska, Soft, Soft rock, Techno); user presets; double-pass option; remembered | M5 | must | verified |
| AU-08 | Dynamic range compressor | RMS/peak blend, attack 1.5–400 ms (25), release 2–800 ms (100), threshold −30–0 dB (−11), ratio 1–20 (4), knee 1–10 dB (5), make-up 0–24 dB (7) | M11 | should | planned |
| AU-09 | Room reverb / spatialiser | Room size, width, wet, dry, damping | M11 | could | planned |
| AU-10 | Stereo widener | Delay 1–100 ms, feedback, cross-feed, dry mix | M11 | could | planned |
| AU-11 | Stereo mode | Stereo / mono / left only / right only / reverse; headphone cross-feed (S); matrix-surround decode (C) | M5 | must | verified |
| AU-12 | Channel layouts & downmix | Mono→7.1 input; standard-coefficient 5.1/7.1→stereo downmix, LFE policy, centre/surround levels; honours the Windows speaker configuration; upmix off by default | M5 | must | verified |
| AU-13 | ReplayGain / R128 | Off / track / album; preamp; fallback gain for untagged files; peak protection; reads ID3 TXXX, Vorbis, APE and Opus R128 tags | M5 | should | verified |
| AU-14 | Loudness normaliser (real-time) | Target level, window; off by default | M11 | should | planned |
| AU-15 | Pitch-preserving speed | WSOLA time-stretch across 0.25–4×; toggle to "chipmunk" mode | M5 | must | verified |
| AU-16 | Pitch shift | ±12 semitones (fine cents) independent of speed | M11 | should | planned |
| AU-17 | Vocal reduction (karaoke) | Centre-channel cancellation with a band limit | M11 | could | planned |
| AU-18 | Visualisations | Spectrum bars, spectrogram, oscilloscope, VU/peak meters, GPU shader visualiser; chosen per session; fullscreen-able | M5/M11 | should | planned |
| AU-19 | Audio-only presentation | Large cover art, metadata, lyrics (META-09), optional visualiser | M6 | must | planned |
| AU-20 | Resampler quality | Fast / normal (default) / high windowed-sinc polyphase | M2 | must | built |
| AU-21 | Language preferences | Ordered list (e.g. `en, fr`), "original language" option, commentary tracks deprioritised | M5 | must | verified |
| AU-22 | Latency compensation | Reads endpoint latency (e.g. Bluetooth) and offsets A/V sync automatically; manual override | M11 | should | planned |
| AU-23 | Device-change resilience | Default device switch, unplug and replug, sleep and resume: playback continues, position kept | M11 | must | planned |
| AU-24 | Loopback / input recording | Record from a capture endpoint or loopback ("what you hear") through Convert | M12 | could | planned |

## Video & rendering

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| VID-01 | Hardware decoding policy | Auto (D3D11VA zero-copy) / off / per-codec; automatic software fallback on failure, with the reason in stats | M3 | must | verified |
| VID-02 | Frame pacing | Flip-model vsync; cadence-correct 23.976/25/29.97 on 60/120/144 Hz; dropped/late accounting; optional refresh-rate matching in fullscreen | M3/M11 | must | built |
| VID-03 | Fullscreen | F, double-click, Esc to leave; target monitor setting (current / specific); fullscreen controller auto-hides (1.5 s default) and the cursor hides after 1 s | M4 | must | verified |
| VID-04 | Window sizing | ¼, ½, 1:1, 2× (Alt+1..4); fit to screen; "resize window to video" setting; autoscale toggle (O) and scale factor (Alt+O / Alt+Shift+O) | M4 | must | verified |
| VID-05 | Aspect ratio | Default, 16:9, 4:3, 1:1, 16:10, 2.21:1, 2.35:1, 2.39:1, 5:4, custom W:H; cycle with A | M4 | must | verified |
| VID-06 | Crop | Presets 16:10, 16:9, 4:3, 1.85:1, 2.21:1, 2.35:1, 2.39:1, 5:3, 5:4, 1:1 (cycle C); per-edge pixel crop keys; automatic black-bar detection | M4/M11 | must | built |
| VID-07 | Zoom & pan | Zoom presets (Z cycles, Shift+Z resets); interactive magnifier (wheel zoom, drag pan) | M11 | should | planned |
| VID-08 | Deinterlacing | Off / Auto (stream flags) / On; discard, blend, bob, linear, motion-adaptive, motion-adaptive double-rate, inverse telecine; cycle with Shift+D, toggle with D | M3 (bob), M11 | must | planned |
| VID-09 | Colour adjustments | Hue −180..180, brightness 0..2, contrast 0..2, saturation 0..3, gamma 0.01..10; reset | M11 | must | planned |
| VID-10 | Sharpen | Strength 0..2 | M11 | should | planned |
| VID-11 | Debanding | Gradient smoothing | M11 | should | planned |
| VID-12 | Denoise | Spatio-temporal; light/medium/strong | M11 | should | planned |
| VID-13 | Film grain | Synthetic grain overlay | M11 | could | planned |
| VID-14 | Transform | Rotate 90/180/270, flip H/V, transpose, anti-transpose; auto-rotate from container display matrices | M3/M11 | must | planned |
| VID-15 | Free rotation | Arbitrary angle | M11 | could | planned |
| VID-16 | Image overlay ("logo") | File, position or anchor, opacity, delay/repeat | M11 | could | planned |
| VID-17 | Text overlay ("marquee") | Text with tokens (time, title, artist, position), anchor, size, colour, opacity, timeout | M11 | could | planned |
| VID-18 | Shader effects pack | Sepia, posterise, invert, edge detect, cartoon, mirror, motion blur, wave, ripple, psychedelic, gradient, colour threshold/extract, puzzle | M11 | could | planned |
| VID-19 | Stereoscopic 3D | SBS/TB → 2D (left eye) or red-cyan anaglyph | M11 | should | planned |
| VID-20 | 360° video | Equirectangular viewer: drag/arrow keys to look, Page Up/Down for field of view, reset; reads spherical metadata from MP4 and Matroska | M11 | should | planned |
| VID-21 | HDR → SDR | PQ and HLG, BT.2020 → 709 gamut map, tone map to target nits (203 default), FP16 pipeline, 10-bit decode end to end | M11 | must | planned |
| VID-22 | HDR passthrough | HDR10 metadata to an HDR-enabled display (Advanced Color) | M15 | should | planned |
| VID-23 | Colour pipeline correctness | BT.601/709/2020 matrices, limited/full range, chroma siting, flagged-vs-guessed colorimetry | M3 | must | verified |
| VID-24 | Scaling quality | Bilinear / Catmull-Rom / Lanczos3; chroma upsampling quality | M11 | should | planned |
| VID-25 | Late-frame policy | Drop late frames (default on), skip-to-keyframe when far behind, stats | M3 | must | verified |
| VID-26 | Title on start | OSD shows the title for 3 s (configurable, can be disabled), position setting | M4 | should | verified |
| VID-27 | Always on top | Never / always / while playing | M4 | must | verified |
| VID-28 | Picture-in-picture | Compact always-on-top mini window with minimal controls | M15 | should | planned |
| VID-29 | Power management | Display stays awake while video plays; audio-only blocks system sleep but not the screen saver; released when paused or stopped | M4 | must | built |
| VID-30 | Frame export | Export the current frame, or every Nth frame of a range, to images | M11 | could | planned |
| VID-31 | Stats overlay | Decoder (own/MF/hardware), resolution, fps, dropped/late, A/V offset, bitrates, buffer, colour info | M4 | should | verified |

## Subtitle & OSD rendering

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| OSD-01 | Text subtitle style | Font family with per-script fallback, relative size 50–400% (presets smaller/small/normal/large/larger), colour, opacity, bold, outline none/thin/normal/thick + colour, shadow colour/offset/opacity, background box colour/opacity | M5 | must | verified |
| OSD-02 | Subtitle scaling keys | Ctrl+wheel, Ctrl+0 reset; menu 50/75/100/125/150/200% | M5 | must | built |
| OSD-03 | Placement | Vertical margin; force position; subtitles in the letterbox bars | M5 | should | verified |
| OSD-04 | Dual subtitles | Secondary track shown at the same time (top or bottom) | M5 | should | verified |
| OSD-05 | ASS style policy | Respect / scale / override styles; embedded fonts honoured | M5 | must | built |
| OSD-06 | System caption settings | Honours Windows Settings → Accessibility → Captions by default | M15 | should | planned |
| OSD-07 | OSD messages | Volume, position, speed, track names, delays, aspect/crop, A-B; duration and position settings; can be disabled | M4 | must | verified |
| OSD-08 | Forced-only mode | Show only forced subtitles (DVD/BD/PGS/flagged tracks) | M7 | should | planned |
| OSD-09 | Complex scripts & RTL | DirectWrite shaping for Arabic, Hebrew, Indic and CJK, bidi | M5 | must | built |
| OSD-10 | Bitmap subtitle scaling | Scale or recolour PGS/VobSub | M11 | could | planned |

## Network input, discovery & casting

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| NET-01 | HTTP/HTTPS progressive | Range requests, keep-alive, redirects (limit 10), gzip-agnostic, reconnect with resume, custom user-agent/referrer/cookies/headers per URL, system proxy + explicit proxy, TLS via OS (SChannel through `SslStream`), certificate errors shown with an explicit, remembered per-host override | M8 | must | planned |
| NET-02 | Internet radio (ICY/SHOUTcast/Icecast) | `Icy-MetaData` titles into now-playing, reconnect, chained Ogg | M8 | must | planned |
| NET-03 | HLS | VOD + live + event; TS and fMP4 segments; AES-128 and SAMPLE-AES (non-DRM); alternate audio/subtitle renditions; WebVTT segments; adaptive bitrate (throughput + buffer based) with a manual quality pin; live-edge seeking and DVR window; discontinuity handling | M8 | must | planned |
| NET-04 | MPEG-DASH | Static + dynamic MPD, SegmentTemplate/Timeline/List/Base, multi-period (C), ABR; non-DRM | M12 | should | planned |
| NET-05 | Smooth Streaming | — | post | could | post-1.0 |
| NET-06 | RTSP | RTP over UDP with TCP-interleaved fallback (and a forced-TCP option); Basic/Digest auth; keep-alive; H.264/H.265/AAC/MPEG-audio/PCM/JPEG depacketisers; RTCP sender reports for sync | M12 | must | planned |
| NET-07 | RTP / UDP | Unicast + multicast (`udp://@239.x.x.x:port`), TTL and interface selection, MPEG-TS over UDP/RTP, jitter buffer, reorder, FEC Could | M12 | must | planned |
| NET-08 | FTP / FTPS | Passive mode, resume, explicit TLS | M12 | should | planned |
| NET-09 | SMB / Windows shares | `\\server\share` and `smb://` mapped to UNC through the OS; browse in the sidebar; credentials through the Windows credential prompt | M6 | must | planned |
| NET-10 | WebDAV | Over the HTTP stack | post | could | post-1.0 |
| NET-11 | SFTP / NFS / SRT / RIST | Large protocol work for little desktop demand | post | could/wont | post-1.0 |
| NET-12 | Open Network Stream dialog | URL box with history (per-user, clearable), paste-and-play (Ctrl+V anywhere), advanced options (caching ms, start time, user-agent) | M8 | must | planned |
| NET-13 | Caching controls | File 300 ms, network 1500 ms, live 1500 ms defaults; per-open override; buffer state shown | M8 | must | planned |
| NET-14 | Link resolvers | Extension point that turns a web-page URL into stream URLs; none bundled for third-party sites (terms-of-service risk); the API is documented for user extensions | M14 | should | planned |
| NET-15 | Podcasts | Subscribe by RSS/Atom URL, refresh, episode list, download for offline, resume per episode | M8 | should | planned |
| NET-16 | Radio directory | Opt-in browsing of a public station directory through a provider extension | M14 | could | planned |
| DISCO-01 | UPnP/DLNA media server browsing | SSDP discovery, ContentDirectory browse/search, thumbnails | M10 | should | planned |
| DISCO-02 | mDNS/DNS-SD | Finds cast targets and media services | M10 | must | planned |
| DISCO-03 | SAP/SDP announcements | Multicast session listings | M12 | could | planned |
| CAST-01 | Renderer menu | "Play on…" lists discovered DLNA renderers, cast devices and REX Flint receivers; local playback pauses and hands over position | M10 | must | planned |
| CAST-02 | DLNA renderer push | AVTransport SetURI/Play/Pause/Seek/Stop, RenderingControl volume, a local HTTP server serving the original file or a remux | M10 | must | planned |
| CAST-03 | Cast-protocol receiver push | TLS cast channel, LOAD/PLAY/PAUSE/SEEK/volume; direct play when the receiver supports the codecs, otherwise remux (stream copy) and only then transcode through MF H.264+AAC; burned-in subtitles when transcoding (C) (behind an experimental setting) | M10 | should | planned |
| CAST-04 | REX Flint receiver | Cast to Fire TV devices running the REX Flint receiver over the REX wire protocol (shared golden vectors with the Flint repo), a family integration no other player has | M10 | could | planned |
| CAST-05 | Cast quality settings | Max resolution/bitrate for transcode, "prefer remux", conversion speed vs quality | M10 | should | planned |

## Discs & capture

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| DISC-01 | Audio CD | TOC, track list, gapless track transitions, CD-Text (S), paranoia-style re-read on errors (C), pre-emphasis flag de-emphasis (C) | M13 | must | planned |
| DISC-02 | CD rip | Through Convert: WAV/FLAC/MP3/AAC, filename pattern from track metadata | M13 | should | planned |
| DISC-03 | CD metadata lookup | Opt-in online lookup through a provider extension | M14 | could | planned |
| DISC-04 | DVD-Video (unencrypted) | Disc, `VIDEO_TS` folder, ISO; title/chapter navigation, audio/subtitle stream selection, angles (C), forced subtitles, LPCM/AC-3/DTS/MPEG audio | M13 | must | planned |
| DISC-05 | DVD menus | Navigation VM, highlights, buttons | post | could | post-1.0 |
| DISC-06 | Blu-ray (unencrypted BDMV) | Disc, `BDMV` folder, ISO; playlist (mpls) selection with main-feature heuristic, chapters, PGS, multi-clip seamless joins | M13 | should | planned |
| DISC-07 | Blu-ray menus (HDMV/BD-J) | — | post | wont | post-1.0 |
| DISC-08 | VCD/SVCD | MPEG-1/2 in Mode 2 sectors | M13 | could | planned |
| DISC-09 | Encrypted-disc message | Detects CSS/AACS and says plainly it is unsupported (§5.5) | M13 | must | planned |
| DISC-10 | Disc auto-detection | Media arrival → "Play disc" prompt or AutoPlay handler | M13/M15 | should | planned |
| CAP-01 | Webcam / capture card video + audio | MF device sources (formats, resolution, fps pick), audio endpoint pairing; device config dialog where the device exposes properties | M12 | should | planned |
| CAP-02 | Screen capture | Monitor or window, fps, region, cursor draw, follow-mouse region; Windows.Graphics.Capture first, DXGI duplication fallback (house knowledge from Flint) | M12 | should | planned |
| CAP-03 | Capture to file / stream | Through Convert/Stream-out | M12 | should | planned |
| CAP-04 | TV tuners (DVB via BDA) and analog TV | — | post | wont | post-1.0 |

## Convert, record, stream-out

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| OUT-01 | Convert dialog / wizard | Sources (files, disc, network, capture), profile, destination (folder + name pattern, "-converted" suffix default, overwrite policy), batch queue with progress/ETA/cancel, "convert and play" option | M9 | must | planned |
| OUT-02 | Built-in profiles | Video H.264+AAC (MP4); Video H.265+AAC (MP4, when an encoder exists); Video H.264+AAC (MKV); Video for web H.264+AAC fragmented MP4; Audio MP3; Audio AAC (M4A); Audio FLAC; Audio WAV; Audio ALAC (M4A); Remux only (copy all streams into MP4/MKV/TS) | M9 | must | planned |
| OUT-03 | Profile editor | Container, video codec/bitrate or quality/resolution/fps/deinterlace/crop/rotate, audio codec/bitrate/channels/rate, subtitles copy or burn-in, metadata copy; import/export profiles (JSON) | M9 | should | planned |
| OUT-04 | Stream copy (remux) | Bit-identical elementary streams, timestamps preserved, chapters/metadata/attachments carried | M9 | must | planned |
| OUT-05 | Trim / clip | Convert a range (A–B) with smart keyframe handling (stream copy where aligned) | M9 | should | planned |
| OUT-06 | Record | Record button / Shift+R: writes the incoming stream (copy) from now until stopped; for files records the played range; destination folder setting; default container chosen per input | M9 | must | planned |
| OUT-07 | Stream-out: HTTP server | Serve TS / fragmented MP4 / WebM over HTTP to the LAN (opt-in, bind address, port, password optional) | M9 | should | planned |
| OUT-08 | Stream-out: RTP/UDP | MPEG-TS over UDP or RTP unicast/multicast, TTL, SDP file generation, SAP announce (C) | M9 | should | planned |
| OUT-09 | Stream-out: Icecast source | Push audio to an Icecast/SHOUTcast server (MP3/AAC/Ogg) | M12 | could | planned |
| OUT-10 | Stream-out: RTSP server | — | post | could | post-1.0 |
| OUT-11 | Transcode-while-streaming | Same graph as Convert with real-time pacing; "display locally while streaming" toggle | M9 | should | planned |
| OUT-12 | Stream chain string | A human-readable, versioned JSON "output chain" document equivalent (for CLI/automation): `{ "transcode": {...}, "outputs": [ {"file": ...}, {"http": ...} ] }` | M9 | should | planned |
| OUT-13 | Scheduled broadcasts (multi-channel manager) | — (CLI + Task Scheduler recipe documented instead) | post | could | post-1.0 |
| OUT-14 | Own audio encoders | PCM/WAV, FLAC (own); MP3/AAC/WMA via MF; Opus/Vorbis encoders own (C, post-1.0) | M9 | must | planned |

## Playlist, library & persistence

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| LIB-01 | Playlist pane | Docked / undocked / hidden (Ctrl+L, Ctrl+U); columns: title, duration, artist, album, track, genre, year, file name, folder, type, size, date added, play count, last played (choose and reorder); click-to-sort, instant search filter, drag reorder, multi-select, remove, keep-only-selected, clear (Ctrl+W), jump to playing | M4/M6 | must | built |
| LIB-02 | Adding media | Open file(s) Ctrl+O, folder Ctrl+F, disc Ctrl+D, network Ctrl+N, capture Ctrl+C, paste location Ctrl+V; drag-and-drop (drop plays, Ctrl+drop enqueues, drop on the list inserts at that position); recursive folder expansion with natural sort; playlist files expand in place | M4 | must | verified |
| LIB-03 | Save / load playlists | Ctrl+Y / Ctrl+X in M3U8, XSPF or PLS; the current queue is restored at startup (setting, default on) | M6 | must | planned |
| LIB-04 | Named playlists | Create, rename, duplicate, delete; add to playlist from anywhere | M6 | must | planned |
| LIB-05 | Media library | Watched folders (opt-in suggestions: Videos, Music, Pictures); incremental scanning with change notifications plus a periodic rescan; low-priority background probing (duration, tags, thumbnails); views: Videos (thumbnail grid; series/season grouping C), Music (artists, albums, tracks, genres), Pictures (S), Recent, Playlists, Podcasts; library-wide search; play counts and last played; "Continue watching" row | M6 | must | planned |
| LIB-06 | Sidebar sources | Playlist, Library, Discs, Local network (DLNA servers, Windows shares, SAP), Internet (podcasts, provider extensions) | M6/M10 | must | planned |
| LIB-07 | Recent media | Menu + jump list; clear; "do not keep history" privacy switch | M6 | must | planned |
| LIB-08 | Playlist quick slots | Ctrl+Shift+1..9 sets, Ctrl+1..9 plays a remembered playlist position | M6 | could | planned |
| LIB-09 | Missing files | Greyed-out entries with "Locate…", and relink by folder | M6 | should | planned |
| LIB-10 | Library backup / export | Export or import the library and playlists (JSON + M3U8) | M6 | should | planned |
| LIB-11 | Crash-safe store | RexStore (§7.7): torn writes never lose more than the last record | M6 | must | planned |
| LIB-12 | Ratings, duplicates finder | — | post | could | post-1.0 |

## UI surface

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| UI-01 | Main window | Video surface; seek bar (elapsed/remaining toggle on click, chapter ticks, buffered ranges, hover thumbnails); transport (play/pause, stop, previous, next, fullscreen, playlist, effects, loop, shuffle); volume + mute; status chips (hardware decode, cast target, recording, speed) | M4 | must | verified |
| UI-02 | Menu system | Classic menu bar (toggle) + command bar. Media: open file/multiple/folder/disc/network/capture, open location from clipboard, recent media, save playlist, convert/save, stream, quit at end of playlist, quit. Playback: title, chapter, program, bookmarks, play on (renderer), speed, jump forward/back, go to time, play/stop/previous/next, record, A-B loop, frame step, sleep timer. Audio: track, device, stereo mode, visualisation, volume up/down, mute. Video: track, fullscreen, fit window, zoom, aspect, crop, deinterlace + mode, snapshot, always on top, transform, 360°, stats overlay. Subtitle: add file, track, secondary track, text scale, delay and sync. Tools: effects and filters, track synchronisation, media information, codec information, program guide, log console, extensions, customise interface, preferences, keyboard shortcuts. View: playlist, dock playlist, library, minimal interface, fullscreen interface, picture-in-picture, always on top, status strip. Help: help, shortcuts, what's new, check for updates, report a problem, about | M4–M15 | must | built |
| UI-03 | Video context menu | The same tree in context form (play/pause, stop, previous/next, record, jumps, speed, audio, video, subtitle, playback, tools, view, open media, play on, quit), reachable in minimal and fullscreen modes | M4 | must | built |
| UI-04 | Fullscreen controller | Seek bar, transport, volume, tracks, effects, leave fullscreen; auto-hide (1.5 s), opacity, keyboard-focusable, touch-friendly | M4 | must | verified |
| UI-05 | Minimal interface | Ctrl+H hides chrome; right-click keeps every menu reachable | M4 | must | verified |
| UI-06 | Mini player & PiP | Compact audio player and the video PiP window (VID-28) | M15 | should | planned |
| UI-07 | Effects & filters panel (Ctrl+E) | Audio tab (equaliser, compressor, reverb, widener, pitch & speed, advanced); Video tab (essential, crop, colours, geometry, overlays, advanced); Synchronisation tab (audio delay, subtitle delay/speed, sync marks); presets save/load; per-file "remember these settings" option | M5/M11 | must | built |
| UI-08 | Media information (Ctrl+I) | General (editable tags + artwork), Metadata (all raw tags), Codec (per stream: codec + decoder used, language, channels, sample rate, bit depth, resolution, fps, pixel format, colour primaries/transfer/matrix/range, rotation, HDR metadata), Statistics (input and demux bitrate, discontinuities, decoded/displayed/late/dropped frames, audio decoded/played/lost buffers, buffer level, network throughput), Location; Ctrl+J opens the Codec tab | M4/M6 | must | built |
| UI-09 | Log console (Ctrl+M) | Verbosity (error/warning/info/debug), source filter, search, copy, save, clear; "Copy diagnostics bundle" | M4 | must | verified |
| UI-10 | Preferences | Simple pages (Interface, Playback, Audio, Video, Subtitles & OSD, Input & codecs incl. a decode-ladder view, Network & privacy, Library, Hotkeys, Extensions, Advanced) + All settings: searchable, generated from the typed schema, "modified only" filter, per-setting reset; reset all; import/export | M4/M6 | must | built |
| UI-11 | Hotkey editor | Rebind any command; conflict detection; per-binding global toggle; mouse settings (wheel = volume / seek / none, horizontal wheel, middle-click and back/forward buttons); reset; export | M6 | must | planned |
| UI-12 | Customise interface | Toolbar editor: palette of every transport button (frame step, A-B, record, snapshot, loop, shuffle, speed, playlist, fullscreen, effects, stop, chapter nav, cast), drag to order, save/restore layouts | M14 | should | planned |
| UI-13 | Themes | System / light / dark; accent from the system or the REX signal colour; high contrast; Mica backdrop; theme packages (C) | M4/M15 | must | built |
| UI-14 | First run | Welcome, privacy choices (metadata lookups off by default, update checks on with an opt-out), file-association offer, short tour (house first-run tour pattern) | M4/M15 | must | verified |
| UI-15 | What's new | Shown once after an update (house pattern) | M4 | should | verified |
| UI-16 | Window title | `<title> — rexplayer`; token format configurable | M4 | should | verified |
| UI-17 | In-window confirmations | No modal pop-ups during playback; system dialogs only before a window exists (house rule) | M4 | must | built |
| UI-18 | Mouse & touch | Wheel volume (default) or seek; double-click fullscreen; middle-click pause; back/forward buttons = previous/next; touch: tap shows controls, double-tap left/right edge seeks ∓10 s, pinch zoom in zoom/360° modes | M4/M15 | must/should | built |

## Tools, interfaces, extensions & CLI

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| TOOL-01 | `rexplay` CLI (human mode) | Verbs: play, enqueue, pause, resume, stop, seek, status, next, previous, volume, tracks, snapshot, convert, probe, devices, settings get/set, keys, extensions, version, help. Flags: `--start/--stop/--run-time`, `--fullscreen`, `--no-video/--no-audio`, `--audio-device`, `--sub-file`, `--audio-delay/--sub-delay`, `--rate`, `--volume`, `--loop/--repeat/--shuffle`, `--play-and-exit`, `--new-instance`, `--enqueue`, `--log-file`, `--verbose`, `--output <chain.json>`, headless test sinks `--vout capture --aout wav:<dir>` | M1–M14 | must | built |
| TOOL-02 | Machine mode | `rexplay agent <cmd>` / `--json`: exactly one JSON document, `protocolVersion`, `capabilities` self-description, exit 0/1, no prompts (house contract) | M0+ | must | verified |
| TOOL-03 | Local automation / remote-control interface | Named-pipe JSON-RPC + event subscription (§9.2), current-user only, size-bounded, deadline-controlled (house IPC invariants); optional localhost text console (C) | M4 | must | verified |
| TOOL-04 | HTTP web remote | Off by default; password required; LAN bind with an explicit choice; mobile-friendly page (transport, seek, volume, playlist, library browse); JSON API over the same command bus; CSRF tokens; rate-limited login | M14 | should | planned |
| TOOL-05 | Global hotkeys | Opt-in per binding (RegisterHotKey); conflicts reported | M14 | should | planned |
| TOOL-06 | Single instance | "Use only one instance" (default on), "enqueue instead of play" (default off), explicit `--new-instance` | M4 | must | verified |
| TOOL-07 | Extensions | §7.8 model; extension points: commands/menus, playback observers, metadata/art/lyrics providers, link resolvers, subtitle providers, service-discovery providers, visualisers (C); manager UI (enable/disable/remove, declared capabilities shown at install); install from local `.rexext` packages; sample extensions + SDK docs + template in the repo | M14 | must | planned |
| TOOL-08 | Program guide | EPG from transport-stream EIT tables | M12 | could | planned |
| TOOL-09 | Updater | Weekly check (off / daily / weekly), in-window notice, download + sha256 verify, install on consent at exit, release notes shown | M4 | should | verified |
| TOOL-10 | Diagnostics bundle | Logs + redacted settings + system/codec report → zip for issue reports | M4 | should | verified |
| TOOL-11 | System probe | `rexplay probe --system`: MF decoders/encoders present, hardware decode caps per codec/resolution, audio endpoints, displays/HDR state | M3 | must | verified |
| TOOL-12 | Portable mode | Settings and library beside the exe when `rexplayer.portable` exists | M15 | should | planned |
| TOOL-13 | Broadcast manager (scheduled multi-channel outputs) | — | post | could | post-1.0 |

## Windows platform integration

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| WIN-01 | Distribution | Per-user Inno installer (no admin), portable zip, winget manifest at 1.0 | M0/M15 | must | built |
| WIN-02 | File associations | Groups: Video (mp4 m4v mkv webm avi mov wmv asf ts m2ts mts mpg mpeg vob flv 3gp ogv y4m), Audio (mp3 flac m4a aac wav ogg oga opus wma aif aiff caf ac3 eac3 dts mka), Playlists (m3u m3u8 pls xspf cue asx wpl), Disc images (iso); per-user ProgIDs + RegisteredApplications capabilities so rexplayer appears in Default Apps; never hijacks: the installer task deep-links to Default Apps | M15 | must | planned |
| WIN-03 | Explorer verbs | "Play with rexplayer", "Add to rexplayer playlist" (classic menu, HKCU); modern top-level Windows 11 menu through a sparse package (C, post-1.0) | M15 | must | planned |
| WIN-04 | AutoPlay handlers | Audio CD, DVD movie, Blu-ray movie, removable-media folders | M15 | should | planned |
| WIN-05 | System Media Transport Controls | Media overlay (title/artist/artwork/timeline), media keys, lock screen, Bluetooth headset buttons, Game Bar media widget | M15 | must | planned |
| WIN-06 | Taskbar | Thumbnail toolbar (previous, play/pause, next), progress state (normal/paused/error), live thumbnail | M15 | must | planned |
| WIN-07 | Jump list | Recent media, pinned items, tasks (open file, resume last, open network stream); classic `ICustomDestinationList` (works unpackaged) | M15 | should | planned |
| WIN-08 | Toasts on track change | Opt-in (Windows App SDK app notifications, unpackaged registration) | M15 | could | planned |
| WIN-09 | High DPI | PerMonitorV2 everywhere incl. the OSD and subtitles; crisp on monitor moves | M4 | must | built |
| WIN-10 | Theme & materials | Follows system light/dark live; Mica | M4 | must | built |
| WIN-11 | Power awareness | VID-29 power requests; pause on sleep, re-open devices on resume | M4/M11 | must | built |
| WIN-12 | Architectures | x64 (M); ARM64 native (S) through cross-platform `Vector128` SIMD paths | M0/M15 | must/should | planned |
| WIN-13 | Minimum OS | Windows 10 1809 (build 17763) and Windows 11; Windows 11 is the primary target | M0 | must | built |
| WIN-14 | Crash recovery | Global handlers (house), "rexplayer closed unexpectedly — resume where you were?" on next start, opt-in local minidumps, nothing uploaded | M4 | must | verified |
| WIN-15 | Share / "Open with" from apps | — | post | could | post-1.0 |
| WIN-16 | URL protocol handler | Deliberately not registered (attack surface without user value) | — | wont | post-1.0 |

## Accessibility & internationalisation

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| A11Y-01 | Named, automatable controls | Every reachable control has an AutomationId + Name/HelpText; a RepositoryTest + UIA tree audit enforce it (house invariant) | M4+ | must | verified |
| A11Y-02 | Keyboard completeness | Every journey works keyboard-only; visible focus; logical tab order; F6 cycles regions | M4+ | must | built |
| A11Y-03 | Screen-reader announcements | UIA notification events for play/pause, volume, seek results, track changes, errors; T announces the position | M15 | must | planned |
| A11Y-04 | High contrast | Every brush key has a high-contrast answer (house parity test) | M15 | must | planned |
| A11Y-05 | Text scaling & reduced motion | Honours Windows text size and animation settings | M15 | must | planned |
| A11Y-06 | Audio description preference | Prefer audio-description tracks when enabled | M15 | should | planned |
| I18N-01 | Localisable resources | `.resw` pipeline, no concatenated sentences, plural categories from a small CLDR rules table, pseudo-localisation build in CI | M15 | must | planned |
| I18N-02 | Right-to-left UI | FlowDirection mirroring | M15 | should | planned |
| I18N-03 | Language picker | System default + override | M15 | should | planned |
| I18N-04 | Shipped languages | 1.0 ships English (house British spelling); translations accepted post-1.0 | M15 | must | planned |
| I18N-05 | Culture handling | Locale-aware display; invariant culture for files, protocols and logs | M1+ | must | built |

## Privacy, security & diagnostics

| ID | Capability | Details | Milestone | Priority | Status |
|---|---|---|---|---|---|
| PRIV-01 | No telemetry | No analytics, no account, no crash upload, ever | M0 | must | built |
| PRIV-02 | Network policy | Network only for user-opened URLs, update checks (opt-out), opt-in metadata, and discovery while the cast/network panels are in use | M4 | must | built |
| PRIV-03 | History controls | Turn off recent/resume history; clear everything | M6 | must | planned |
| PRIV-04 | Credentials | Windows Credential Manager only, never plain text | M8 | must | planned |
| SEC-01 | Hostile-input hardening | Fuzzing (§9.10); bounds-checked spans; configurable allocation caps (box/element size, track count ≤ 128, frame size ≤ 16384², subtitle events, playlist entries); per-parse time budgets; `unsafe` only in Interop and audited SIMD kernels (RepositoryTest) | M1+ | must | built |
| SEC-02 | Decoder isolation | MF transforms run in-process at 1.0; out-of-process decode host is a post-1.0 hardening option | post | could | post-1.0 |
| SEC-03 | Extension trust | Capability manifest shown at install; quarantine on faults; documented "trusted code" stance | M14 | must | planned |
| SEC-04 | Network services off by default | Web remote and stream-out need explicit enabling and show a firewall explanation | M9/M14 | must | planned |
| SEC-05 | Update integrity | HTTPS, owner-pinned release URL, sha256 verification; Authenticode once signing exists (§11) | M4 | must | verified |
| SYS-01 | Logging | House rotating logger (never throws, critical always written) + ETW EventSource | M1 | must | built |
| SYS-02 | Settings safety | Atomic writes, cross-process lock, `.rex-backup`, schema version + migrations, Normalize() clamps (house patterns) | M4 | must | built |
