# Changelog

Every version of rexplayer that reaches users, newest first. A version is published by the CI
pipeline the moment its number reaches `main`.

## 0.6.0 - 2026-10-08

### Added

- **Open with rexplayer.** The installer adds rexplayer to Explorer's Open with for every video,
  audio and playlist file it plays, and to Windows' Default apps; nothing that already opens your
  files is changed. Its last box opens Default apps to make rexplayer your player if you want.
  Opening several files at once from Explorer puts them in one playlist.
- **Updates that carry on.** An update installed from inside rexplayer opens it again afterwards,
  with your playlist back and an offer to carry on where you were.
- **Playlists.** Open M3U, M3U8, PLS, XSPF, cue sheets, ASX and WPL playlists (Ctrl+X, or drop
  them on the window), and save the playlist as M3U8, XSPF or PLS (Ctrl+Y). A cue sheet's tracks
  play as tracks of the one album file, without a gap between them.
- **Carry on where you left off.** Open something you stopped part-way through and rexplayer
  offers to go back there (or always does, or never: Preferences, Memory). The playlist comes back
  when rexplayer starts.
- **Bookmarks** (Ctrl+B), listed under Playback, Bookmarks to go to, rename or delete.
- **Quick slots.** Ctrl+Shift+1 to 9 keeps what is playing and where; Ctrl+1 to 9 goes back to it.
- **Recent media** under Media, Recent media. History can be cleared (View, Clear the history) or
  not kept at all (Preferences, Memory).
- **Stop or pause after this item**, and a **sleep timer** (Playback menu) that fades the sound
  out and pauses or stops after 15 minutes to 2 hours, or at the end of the item.

- **Vorbis sound.** WebM and Matroska files with Vorbis sound now play with it: rexplayer has its
  own Vorbis decoder. A video whose sound still cannot be decoded plays its pictures in silence,
  and the window says why, instead of refusing the whole file.
- **Zoom and pan.** Alt with the mouse wheel zooms into the picture at the pointer; drag to move
  about, or use Alt with the arrow keys. Alt+Plus and Alt+Minus step the zoom, Alt+0 shows the
  whole picture again. While zoomed, a navigator in the corner shows the whole picture with the
  part in view marked: click or drag in it to look somewhere else (Alt+N turns it off).
- **Detailed logs.** rexplayer writes down what it does, so a problem can be diagnosed from the
  log alone: Help, Open the log folder.
- **The mouse.** The middle button plays or pauses; the back and forward buttons go to the previous
  and next item. In full screen the pointer hides along with the controls.

### Fixed

- **Space played or paused and also opened a menu** (or pressed a button a second time) when a menu
  or button had the focus.
- **Closing the window while a dialog was open could crash rexplayer.**
- **Dragging the seek bar jumped about** as the playing position pulled the thumb back.
- **The playlist scrolled back to the top and lost its selection** each time the playing item changed.

## 0.5.0 - 2026-10-07

### Added

- **Subtitles.** Subtitle files beside a film show with it: SubRip (.srt), WebVTT, ASS and SSA,
  MicroDVD, MPL2, SubViewer and SAMI, found by name in the film's folder or a Subs folder, with
  the language read from names such as `film.en.srt`. Subtitles inside MKV and MP4 files show as
  well. Drop a subtitle file on the window, or use Subtitles, Add a subtitle file, to add one.
- **Subtitle controls.** V picks the next subtitle track and Shift+V turns them on or off; G and
  H move them 50 ms earlier or later; Alt+V shows a second track at the top at the same time.
  Ctrl+Plus, Ctrl+Minus and Ctrl with the mouse wheel change their size, and Ctrl+0 puts it back.
- **How subtitles look.** Preferences sets their font, size, colour, opacity, outline, shadow
  and background box, the gap from the edge of the picture, whether they sit in the black bars
  below a wide film, and whether a subtitle file's own colours are used. Older subtitle files
  that are not in Unicode are read in the code page you choose.
- **Languages.** Tell rexplayer which languages you prefer, such as "ja, original" for sound and
  "en, fr" for subtitles, and it picks those tracks; "original" is the language the film was
  made in, and a commentary only plays when you ask for it.
- **The equaliser and effects** (Ctrl+E): ten bands with presets, a preamp, mono, left, right or
  swapped channels, and loudness evening from ReplayGain tags.
- **Playback speed.** Plus and Minus play faster or slower, from a quarter to four times, without
  changing the pitch; ] and [ change it a little; Equals returns to normal.
- **Sound in step.** J and K move the sound 50 ms earlier or later than the pictures.
- **Visualisations.** Music without pictures shows a spectrum, an oscilloscope, level meters or
  a spectrogram; Z changes which.

## 0.4.0 - 2026-10-07

### Added

- **The player window.** rexplayer now opens in a window of its own: video drawn by the graphics
  card, a seek bar, play, pause, stop, previous and next, shuffle and repeat, a playlist, volume
  and mute, full screen, always on top, and a menu with every command and its shortcut. Drop
  files on it to play them (hold Ctrl to add them to the playlist). The window keeps your volume,
  modes and size for next time.
- **One window.** Opening a file while rexplayer is running plays it in the window you already
  have.
- **Snapshots and statistics.** Shift+S saves the picture on screen to Pictures\rexplayer, and
  Ctrl+Shift+I shows what is decoding the media and how well it is keeping up.
- **Video without sound** now plays.
- **Audio tracks.** B switches to the next audio track of media that has several.
- **Aspect ratio and crop.** A and C step through the common shapes, for media recorded with
  the wrong shape or with black bars in the picture itself.
- **Right-click the picture** for the commands you reach for most, even in full screen.
- **Preferences** (Ctrl+P): jump sizes, volume steps and the loudest volume, theme, always on
  top, the full-screen controls, messages over the picture, and one window.
- **A welcome** on the first start, with rexplayer's one privacy question: whether to look for
  new versions. After an update, "What's new" shows what changed.
- **Updates.** rexplayer can look for a new version weekly, daily or only when asked. It installs
  one only when you agree, and only if the download matches its published checksum.
- **Crash recovery.** If rexplayer did not close properly, it offers to carry on where you were.
- **The log and diagnostics.** Ctrl+M shows the log; Help, Save diagnostics makes a zip to
  attach to a problem report, with your home folder hidden. Nothing is sent anywhere.
- **Scripting.** The running window takes commands through a pipe of its own, one JSON line at
  a time, for scripts and agents.

### Fixed

- **Playlists made on Windows** show their songs' names on any system.
- **A decoder that fails unexpectedly** no longer closes rexplayer; the next decoder is tried.
- **Settings files missing some settings** (from an older version, or edited by hand) keep the
  defaults for those instead of turning them to zero.
- **Media that fails part-way** now moves on to the next item.
- **Damaged video headers.** An H.264 or HEVC stream whose header declares counts or sizes beyond
  what the format allows is now refused as damaged, with a message naming the field, instead of
  failing with an internal error.
- **Matroska playback** no longer creates work for the garbage collector on every block it reads.

## 0.3.0 - 2026-10-06

### Added

- **Video.** `rexplay play film.mkv --window` shows the pictures in a window, drawn with Direct3D
  11 and kept in step with the sound; pictures that would arrive too late are dropped and counted.
- **H.264, HEVC and AAC through Windows.** Windows' own decoders handle the codecs rexplayer
  leaves to them; every H.264 and HEVC picture they give matches an independent decoder exactly.
- **MP4 and QuickTime.** Progressive and fragmented files, edit lists for gapless audio, B-frame
  timestamps, iTunes tags and cover art, chapter tracks, display rotation and colour information.
- **Matroska and WebM.** Every lacing form, cues (down to the block a cue names), seek heads,
  live streams with clusters of unknown size, chapters, tags, attached cover art, header
  stripping, and the padding an encoder adds at the end of a stream, which is cut to the sample.
- **Snapshots.** `rexplay snapshot film.mkv --at 1:30` saves the exact picture shown at a moment
  as a PNG.
- **System report.** `rexplay probe --system` lists which codecs rexplayer and Windows can decode
  on this PC, the graphics adapter and the audio output.

### Fixed

- Sound with timestamps a container rounded (Matroska stores milliseconds) no longer loses or
  repeats samples where frames meet.
- With no audio device, the first pictures of a video were dropped while a window opened.

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
