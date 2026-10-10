# Changelog

Every version of rexplayer that reaches users, newest first. A version is published by the CI
pipeline the moment its number reaches `main`.

## 0.15.5 - 2026-10-10

### Fixed

- **Subject Lock recovery.** The lost-subject search now tolerates moderate changes in
  lighting, distance and position, covering apparent size changes from half to twice
  the original selection. Added intermediate size scales after a Windows regression
  exposed a missing range.
- **Tracking safety.** A video resolution change requires a fresh selection rather than
  silently acquiring a different target. The removal preview cannot be re-enabled
  while the selected subject is lost.

### Verification

- Expanded Windows regression cases cover return size, position, frame edges, lighting,
  occlusion, lookalikes and the D3D11 presenter path. Full release CI remains the gate
  for publishing the installable build.

### Limitations

- Subject Lock compares visible image texture rather than semantic identity. It cannot
  guarantee reacquisition through every pose, occlusion or appearance change.

## 0.15.4 - 2026-10-10

### Fixed

- **Silhouette control checks.** UI Automation visibility polling now retries when a
  visualisation rebuilds a control between finding it and reading its screen state.
  The accessible recalibration action remains verified by the desktop tests.
- **Subject reacquisition.** Keeps the original selected texture while a subject is
  absent, searches at several sizes when it returns, and requires two consistent
  sightings before following again. Separate candidates at each size and an
  outline-contrast check help avoid switching to a similar-looking region.

### Improved

- **Auto-follow framing.** Gradually widens the view after losing the selected subject
  and eases back toward the target after a confirmed return. Small frame-to-frame
  location noise no longer makes the camera drift.
- **Verification.** Added Windows tracking tests for prolonged absence, changing
  subject size, temporary matches, and visually ambiguous alternatives; added
  portable tests for steady framing and recovery zoom. The release audit reviewed
  all changes since 0.15.3, and the latest main CI run passed the Windows desktop,
  engine, performance, packaging, website and Linux test jobs.

### Limitations

- Subject Lock matches visible texture, not a person's or object's semantic identity.
  Lighting changes, occlusion and lookalikes can still prevent reliable reacquisition;
  the non-destructive removal preview remains off after tracking is lost.

## 0.15.3 - 2026-10-10

### Fixed

- **Clearing the picture.** Clearing or switching away from video now clears the GPU render target
  to black instead of drawing a cached video texture. A decoder-independent Windows regression
  confirms that later redraws remain black.

### Improved

- **Subject tracking.** Reuses a small template-sampling buffer across consecutive video frames
  without altering the original reference used to reacquire a selected subject. A regression
  checks steady-frame allocations.
- **Video presentation.** Shares the unchanged BGRA colour matrix across draws and paused-frame
  redraws instead of allocating a new matrix each time.
- **Camera memory.** Reuses the temporary capture buffer while processing frames under the
  existing lock, and erases that buffer when capture is disposed.
- **Verification.** Windows builds, formatting, line coverage, adapter tests, UI Automation,
  installer and update tests, portable engine tests, site checks and performance baselines
  passed on the fix pull request.

## 0.15.2 - 2026-10-10

### Added

- **Ghostwire Motion and Surface Shape.** A separately selectable, non-destructive GPU
  visualization reveals fine recorded outlines and softly shaded curvature. Its
  default Surface shape mode works on still images and paused video without needing
  any subject movement or a previous frame.
- **Four visualization modes.** Surface shape highlights visible luminance isolines
  and shaded relief; Hybrid adds motion accents; Contours only isolates spatial
  detail; Motion only highlights actual two-frame differences.
- **Independent saved controls.** Adjust contour sensitivity, motion emphasis and
  short motion-trail length for the new effect. Settings have accessible named modes,
  live previews and independent reset behavior.

### Improved

- **Temporal safety.** Adjacent decoded pictures are compared on the GPU in motion
  modes, with a small spatial match to reduce one-pixel camera shake. Stationary
  modes use no previous-frame storage. Seeks, long gaps, mode switches and size
  changes invalidate stale temporal history; paused redraws do not advance time.
- **Verification.** Windows GPU/WARP and portable tests cover still curved surfaces
  under bright and dim lighting, uniform regions without invented lines, motion
  against stationary features, independent trail settings, discontinuities, and
  restoration of the original frame when the effect is switched off.

### Limitations

- This visualization displays apparent curvature inferred from *visible* pixels;
  it does not recover real 3D shape or hidden anatomy. Strong reflections, shadows,
  poor video detail, compression and camera cuts can distort apparent contours.
  Temporal motion cues are bounded two-frame comparisons, not semantic object
  tracking or dense optical flow.

## 0.15.1 - 2026-10-10

### Improved

- **Subject Lock auto-follow.** Gently centres the selected visible region with a
  contextual zoom capped at 1.85x. The camera responds to a moving target and restores
  the previous framing when follow is switched off. Manual navigation takes priority.
- **Return tracking.** After losing the subject, periodically searches the frame for
  the original visible texture and resumes only on a sufficiently distinctive match.
  Ambiguous lookalikes are rejected; removal does not restart automatically.
- **Subject tools usability.** A responsive, vertically scrollable control panel uses
  a two-column action layout instead of clipping its buttons. Separate Auto-follow
  and Show tracking box switches let the user hide the outline while tracking continues.
- **Camera silhouette.** Better distinguishes similarly bright colours, stabilizes fine
  outlines, raises the mask resolution to 240 by 180, and fits the whole camera picture
  into the visual stage without cutting off its top and bottom. A new recalibration
  button and clearer empty-room instructions help when camera or lighting changes.
- **Detailed video contours.** Neon contours, Ink trace, Topographic contours,
  Chromatic contours, Ghostwire and Ghostwire mask mode detect narrow internal lines,
  gentle recorded shading changes and colour differences that have similar luminance.
  The per-effect sensitivity slider controls the fine-detail response.
- **Quality and verification.** Added GPU readback tests for grey-on-grey texture,
  near-equal-brightness colour edges and original-frame restoration, along with
  tracking, interface, camera mask and framing regressions. The GPU shader and
  renderer sources are separated to meet the repository's file-size gate.

### Limitations

- Contour effects cannot reveal edges missing from the recording; higher sensitivity
  may also illuminate compression noise. Subject Lock matches visible texture rather
  than semantic identity, so prolonged occlusion or lookalikes can still defeat it.
- Camera silhouette is local background separation, not a person-segmentation model;
  it works best after calibrating on an empty, stable room. Ghost Peel remains a
  non-destructive preview, not edited-video export.

## 0.15.0 - 2026-10-10

### Added

- **Ghostwire mask mode.** A separately selectable, adjustable GPU contour effect turns
  visible gradients into fine luminous filaments and a subdued, glass-like image mask.
  Switching it off restores the recorded frame without changing the source file.

### Improved

- **Subject Lock selection.** Dragging a selection no longer conflicts with the picture's
  pointer capture or touch-pan handlers. A keyboard-accessible control can lock the centre
  of the visible picture. Reselecting resets the previous removal preview immediately.
- **Tracking safeguards.** Featureless patches without distinguishing visible detail are
  rejected instead of falsely reporting a reliable lock. Texture matching favours
  continuity when multiple locations look alike.
- **Paused-frame editing.** Selecting and resetting a subject on a decoded video frame
  avoids reapplying YUV colour conversion to an already converted BGRA picture.
- **Verification.** Additional WARP and UI tests cover the separate mask effect, restoring
  the original image, centre selection, weak-texture refusal and re-acquisition. Tests
  and source-size quality gates were updated for the additional effect.

### Limitations

- Ghostwire mask mode only stylizes recorded pixels; it cannot reveal hidden surfaces.
  Subject Lock does not provide semantic person/limb/garment segmentation or guaranteed
  tracking through occlusion. Ghost Peel is a non-destructive preview, not video export.

## 0.14.0 - 2026-10-10

### Added

- **Ghostwire contours.** A near-transparent, dark-glass rendering of the visible video frame,
  with fine luminous cyan contours and secondary colour accents. It is an artistic image-aware
  effect and does not reveal pixels hidden by real-world objects.
- **Colour spotlight and Relief etch.** Colour spotlight responds to a pointed-at colour in
  the current picture; Relief etch adds directional lighting based on visible image contrast.
  Neither effect claims to recognize or track an object's identity.
- **Subject Lock preview.** Select a visible region with a drag and follow its texture as it
  moves. Bounded coarse-to-fine matching follows larger frame-to-frame motion; ambiguous or
  lost matches stop rather than silently attaching to another subject. A visible selection,
  tracking status, reselect and reset controls keep the original picture available.
- **Ghost Peel removal preview.** Reconstruct the selected region from authentic background
  pixels exposed elsewhere in the played footage when the camera is sufficiently steady.
  Where a region is never exposed, use a four-sided background-colour estimate, with
  adjustable feathering and colour tolerance. Preview, show original and reset do not
  change the video file.

### Improved

- **Resume after an in-app update.** One-time recovery restores the ordered playlist, selected
  item, position, playing or paused state, speed, open library source and search, and window
  display choices including fullscreen, crop, aspect and zoom. Recovery checks the installed
  target version; explicit newly opened media takes precedence, and stale update hand-offs
  are discarded.
- **Performance and quality checks.** The selected-region path avoids an extra full-frame
  copy. Regression tests exercise GPU rendering and reset, visible-frame tracking, lost
  targets, seeking, rough selections, inferred fill, and the real Subject Lock controls.

### Limitations

- Subject Lock uses local visible-texture matching and a rectangular, colour-refined mask:
  it is not a semantic person, limb or garment segmentation system, and difficult
  occlusions, camera changes and similar objects can cause tracking to stop.
- Ghost Peel is a **non-destructive preview**, not an editor that exports modified video.
  Unseen regions are only plausible background estimates; they cannot reveal authentic
  surfaces or anatomy that the footage never captured.

## 0.13.1 - 2026-10-10

### Fixed

- **More reliable library artwork.** Damaged cached thumbnails are generated again instead of
  permanently leaving an empty card. Local video posters and folder covers decode from scaled
  file streams without loading the whole original image into managed memory. Slow thumbnail
  decoding follows cancellation when the view changes.
- **Sound-derived cover identity.** Generated music artwork accounts for playback sample rate
  and avoids temporary spectrum-slice allocations during rendering.
- **Library responsiveness.** The post-release audit improved row refresh stability, picture
  loading and cancellation, and direct resumption from the Home shelf's Jump back in cards.
- **Playback and effects stability.** The audit tightened gapless playback timing and object
  lifetimes, visualisation canvas resets, portrait rendering limits and camera cleanup.
- **Smoother update checking.** Installer checksum verification no longer runs on the window
  thread, keeping the interface responsive during a handoff.

## 0.13.0 - 2026-10-10

### Added

- **Eight more real-time GPU picture effects.** Ink trace, Topographic contours and
  Chromatic contours respond to outlines and image structure. Liquid glass, Slice shift,
  Vortex and Cursor lens warp the video in different ways; Edge gravity responds to
  spatial image detail. All effects are non-destructive and work alongside video looks.
- **Organized picture styling.** Browse effects in Contours, Motion & geometry, Interactive
  and Image-aware groups. Effect intensity and detail or movement each have independent
  saved values with controls for previewing, resetting and restoring original playback.
- **Per-episode intro and credits markers.** Mark the start and end of an intro or credit
  segment for each video, then choose Skip Intro or Skip Credits when playback enters a
  confirmed section. Nothing is guessed or skipped automatically: cold opens, recaps,
  alternative intros and post-credit scenes remain under the viewer's control.

## 0.12.0 - 2026-10-10

### Added

- **Collage layout for the library.** A fourth layout choice arranges different-sized artwork
  in dense, justified rows. Pictures load only as tiles become visible, with hover/focus
  priority preserved; the layout choice is remembered for each view.
- **Four optional GPU picture effects.** Prism flow refracts and separates colour, Neon contours
  outlines edges found in the picture, Pixel drift moves quantised image blocks, and
  Kaleidoscope remaps the frame into reflected sectors. The Video menu keeps these separate
  from colour looks. Effects can be switched during playback, adjusted in Preferences and
  turned fully off. Files are never modified.
- **Resonance visualisation.** A new music-driven light field maps spectral bands into layered
  filaments while percussion launches expanding pressure waves. Control colour, shape,
  fluidity, intensity, trails and symmetry in the visualisation settings.

### Improved

- **Camera visualisations.** Foreground masks compensate for exposure changes, join tiny
  silhouette gaps and discard small disconnected noise. Beat Edit can smoothly follow a
  sufficiently confident foreground outline. This is local background subtraction, not
  semantic recognition; a static camera and a clean background still matter.
- **Sharper music graphics.** A higher software drawing ceiling improves definition on
  capable hardware while the existing adaptive quality controller protects responsiveness.
- **Cleaner timed lyrics.** Compact text backdrops replace the broad dark overlay across
  bright visualisations, keeping words readable without hiding the scene.

## 0.11.0 - 2026-10-10

### Added

- **Movies and TV Shows have their own library views.** Filenames identify series, seasons and
  episodes locally, including S01E02 and 1x03 naming; shows open into seasons and episodes in
  viewing order. Standalone movies get a poster-style grid with cleaned-up titles and years when
  available. All videos remain accessible in the original all-videos view.
- **Eight real-time video looks.** Cinema, Clear, Sunset, Arctic, Monochrome, Vintage, Neon and
  Night vision, alongside Original, are selectable from the video menu and Preferences. The GPU
  adjusts the displayed picture without rewriting the source or changing its metadata.
- **Artwork beside videos.** Local posters and folder covers are used when present; otherwise the
  library can show a frame from the video. Hovered and keyboard-focused cards, followed by nearby
  cards, get priority while background thumbnail work remains bounded and cancellable.

### Fixed

- A large library banner image could expand the header until the Home shelves and media lists were
  pushed offscreen. The banner now has a bounded height and browse views retain vertical scrolling.

## 0.10.0 - 2026-10-10

### Added

- **A library to browse, not just a list.** Home opens on shelves of what to play next: jump back
  into videos, recently played, your most played, new in your library, albums, videos and
  pictures. Every view has a banner with its artwork, what it holds and how long it lasts, with
  Play, Shuffle and Add to the playlist. Choose for each view how it is laid out (a list, cards or
  big cards), how big the cards are, what it is ordered by (title, artist, album, year, date added,
  last played, most played, length, either way round) and what it is grouped under (first letter,
  artist, album, genre, year, decade, folder, date added or length), with headings; each view
  remembers. Videos left part-way show how far you got; cards show a play mark under the pointer.
- **Every piece of media has a picture.** Videos Windows has no thumbnail for (WebM, Matroska and
  others) show a frame of the video itself, and the playlist shows every item's picture too.
- **Menus show what is on.** Shuffle, repeat, mute, subtitles, always on top, full screen, the
  playlist, the library, lyrics, statistics, the navigator, the minimal interface and stop or pause
  after this one are ticked when on.
- **Lyrics over any visualisation.** A song's lyrics now show over the visualisation, not instead
  of it; Shift+L (or right-click the visualisation) puts them away and brings them back.

### Improved

- **Every visualisation moves with the music.** Kicks, snares and hi-hats are heard apart, as well
  as the beat, the tempo and the drop, and every effect answers to them: nothing is left to chance.
  Light no longer builds up until the picture goes white: trails fade and bloom glows without ever
  piling up, through a filmic curve. Drawing happens away from the window and adapts to the
  computer, so the window never stutters.
- **Vinyl** is a turntable: grooves that catch the lamp, the song cut into them in light as it plays,
  the sound as a ring round the record, strobe dots that flash on the kick, and a tone-arm that swings
  to the needle.
- **Aurora** is the northern lights: curtains of rays that fold and drift, green at the hem and
  violet above, over mountains and stars, with a wave of light along them on each kick.
- **The beat edit** cuts to the drums: punches in on the kick and swings with it, tilts and glitches
  on the snare, changes grade on the bar, freezes and snaps back, glitters on the hats, and breaks
  into four screens on the drop. Its shadows take the palette's colour.
- **New palettes** (neon, fire, aurora, ocean and sunset), each visualisation starting in the one
  that suits it; Halo and Embers draw streaks of light; the silhouette's outline is smooth.
- Settings that did nothing are gone or now work: the aurora's stars and drift, the ripples'
  origin and when they fall, the strobe's patterns and colours, vinyl's waveform ring and strobe dots.

### Fixed

- The camera visualisations ask before using the camera, wherever they are chosen, and use it
  reliably: switching between them no longer leaves one on its fallback, a camera that fails to
  start is tried again, and the beat edit's picture is four times as detailed.
- Choosing a visualisation while a song had lyrics did nothing.
- Closing the window as the library finished a scan could end in a crash.

## 0.9.0 - 2026-10-09

### Added

- **Artwork made from the music.** Songs without a cover now get a stable, unique image whose
  palette follows the spectrum and whose shapes follow its waveform and dynamics. Preferences has
  controls for the style, colour, detail, contrast and filename influence. Covers are made only as
  they become visible, two at a time, and cached by the file and settings.
- **A live beat edit.** The camera or cover art now punches, cuts, colour-splits, freezes, echoes and
  changes grade with the music. Five styles and all of their controls sit beside every other
  visualisation's settings. Camera pictures stay in memory and never leave the computer.
- **Library backup and restore**, including watched folders, history and named playlists.

### Improved

- Vinyl, halo, mirror wave, aurora, embers, ripples, strobe and camera silhouette now use a shared
  music-aware renderer with beat, tempo and drop tracking, richer motion and more individual
  controls. Lyrics can be hidden so any visualisation fills the presentation.
- Music, video, picture, album, artist and genre views have purpose-built compact, cover-grid,
  thumbnail and gallery layouts. Missing video thumbnails fall back to a decoded frame.
- Opening a large picture yields to the window before decoding, and thumbnail/artwork work is
  bounded, cancellable and cached so scrolling does not stall the app.

### Fixed

- Choosing a video in Continue watching starts at its saved point immediately without a second
  resume question.
- Gapless autoplay now changes the title, duration, picture and seek position exactly when the next
  item's first sample is heard instead of leaving the previous item frozen on screen.
- Right Ctrl works everywhere Left Ctrl does.
- Updating closes rexplayer cleanly, then a tiny hand-off starts the verified installer after the
  process has exited; the window no longer hangs while the installer tries to replace it.

## 0.8.0 - 2026-10-09

### Added

- **Eight new visualisations**, twelve in all. **Vinyl**: a record turning at 33, 45 or 78, its
  groove cut by the music itself, the song's cover as the label and a tone-arm that rides in as
  the song plays. **Halo**: the spectrum radiating from a ring that swells with the bass.
  **Mirror wave**, **Aurora** and **Embers** (sparks the music throws up). **Ripples**: a ring
  bursting outward on every beat. **Strobe**: the screen glowing with the music in the colours you
  choose, kept to three flashes a second unless you allow more. **Camera silhouette**: you, seen by
  the camera, drawn as a glowing outline, a figure filled with the music, or sparks; it asks first,
  records nothing, and the camera is on only while it shows.
- **Settings for every visualisation** (Audio, Visualisation settings, Shift+Z, or right-click the
  visualisation): colours (Windows' accent, following the pitch, a rainbow, warm, cool or one of
  your own), sensitivity, and what each has of its own, such as bars and peaks, ray count, spark
  amount, ring thickness or record speed. Changes show as you make them.
- **Change any shortcut** (View, Keyboard and mouse, Ctrl+K): press the new keys, clear one, or
  give it its own back; a shortcut taken from another command is reported. Tick Everywhere for a
  shortcut that works while another program is in front. Choose what the wheel, the tilted wheel,
  the middle button and the back and forward buttons do, and save the whole set to a file.
- **Pinch to zoom.** Pinching a touchpad (or Ctrl with the wheel) zooms into videos and pictures
  at the pointer, with the whole picture small in a corner; scrolling with two fingers moves about.
  Touch screens pinch and drag too. Sizing subtitles with Ctrl and the wheel is a setting.
- **The timeline shows more.** Bookmarks are marked on it, a click away; with Preferences, Show
  the video frame under the pointer, hovering over it shows the exact picture there. A speed button
  beside the volume shows the rate and offers every speed, with a step to normal.
- **Lyrics you can click.** A timed line of the lyrics takes the song to it.
- **More in the library.** Pictures have a gallery of their own; albums, artists and genres show as
  covers; videos named like Show.S01E02 are marked with their season and episode, and play on with
  the rest of their season; songs credited to several artists appear under each of them.
- **VP9 and 10-bit HEVC** video play with Windows' decoders, and subtitles kept in a folder named
  after the film inside Subs or Subtitles are found.
- **Missing files.** Playlist entries whose files have moved are greyed and marked; right-click,
  Find it, or Look for the missing files in a folder to relink them all at once.
- **Preferences** gains the navigator, the visualisation, and a way to the keyboard and mouse.

### Fixed

- Closing the playlist or choosing the minimal interface while music played left grey strips
  where the black picture area had not grown to fill the window.
- Tooltips named the shortcut rexplayer came with, not the one in use.
- Closing the window as the pointer left the timeline, or with a visualisation showing, could
  crash rexplayer on its way out.

## 0.7.0 - 2026-10-08

### Added

- **Pictures.** JPEG, PNG, GIF, WebP, BMP and TIFF open like any other media, and HEIC and AVIF
  too where Windows has their extensions. Each shows for 5 seconds (Preferences, Playback) before
  the next item, so a folder of pictures plays as a slideshow; an album's cover stays out of its
  playlist. Animated GIFs move, with rexplayer's own GIF decoder; photos stand the right way up,
  as their cameras recorded. Zoom, pan, full screen and snapshots work on pictures as on video.
  The installer adds rexplayer to Open with for pictures too.
- **The media library** (View, Library, or Ctrl+Shift+L). Choose folders and rexplayer keeps up
  with the music and videos in them as files come and go: songs, albums, artists and genres;
  videos with their pictures; what you played or added lately; videos you left part-way; and a
  search across everything. It fills in quietly in the background.
- **Named playlists.** Keep the playlist, or the entries you choose, under a name (Media,
  Playlists), add what is playing to any of them (Playback, Add to a playlist), and play, rename,
  copy or delete them.
- **Cover and lyrics.** Music shows its cover (its own, or a cover.jpg or folder.jpg beside it)
  with its title, artist and album; lyrics from an .lrc file beside the song, or its own tag,
  follow along with the line being sung marked.
- **Ogg and Opus.** .ogg, .oga and .opus files play: Vorbis and FLAC with rexplayer's own
  decoders, Opus with the decoder Windows provides, also inside WebM.
- **Louder.** The volume goes to 200 % out of the box, bending smoothly at the top instead of
  clipping.

### Fixed

- An MP4 whose header gives an impossible length no longer fails to open.

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
