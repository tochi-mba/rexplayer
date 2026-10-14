# Changelog

Every version of rexplayer that reaches users, newest first. A version is published by the CI
pipeline the moment its number reaches `main`.

## Unreleased

### Improved

- **Uninterrupted caption playback mode.** With *Hold for subtitles* enabled, rexplayer now
  waits for the predicted amount of caption coverage needed to watch the remainder without
  rebuffering, based on conservative transcription speed, playback speed and remaining duration.
  It no longer resumes after an arbitrary 90-second maximum. An empty new transcript holds
  before playback rather than first running out of captions. Progress shows the target;
  playback remains manually resumable, and errors release the wait.
- **Caption preparation overlay.** When playback waits for captions, a clear message over the
  picture shows how much subtitle coverage is ready and how much is needed. **Play now** lets
  you start immediately rather than wait for the target.

## 0.24.1 - 2026-10-14

### Fixed

- **Background logging keeps up with a busy processor.** Log entries are written promptly even
  while decoding and other work keep the processor occupied. Writing stays in the background and
  critical entries still reach the file immediately.
- **Smoother progressive transcript buffering.** The buffer learns how long each newly transcribed
  stretch really takes, including variability between chunks and overlapping recognition context.
  When pausing for subtitles is enabled, it waits for a bounded safety reserve instead of
  repeatedly catching up after a single slow chunk, and releases promptly as new subtitles arrive.
- **Visible transcription progress.** A labelled CAPTIONS coverage rail sits below the playback
  timeline instead of drawing a faint mark over it, with the remaining lead beside it. The
  transcription button opens the actual list of queued filenames, the active file's progress,
  each file's completion and quick pause/stop controls.

## 0.24.0 - 2026-10-14

### Added

- **rexplayer fits itself to the computer.** On a slower computer it does a little less so
  playback stays smooth: visualisations are drawn smaller and less often, the heaviest picture
  effects stay off, Subject Lock follows its subject in a half-size copy, transcription uses a
  smaller speech model and waits while something plays, an empty launch prepares only the window,
  the timeline preview and list animations are off, and fewer
  pictures wait to be decoded. Windows' energy saver makes it a step lighter. Preferences has a
  new Performance section showing what was chosen and why, with Automatic, Best quality, Balanced
  and Fastest. A capable computer loses nothing.
- `./dev.ps1 measure` measures the whole player as people feel it (launch, opening each kind of
  media, seeking, steady playback, memory, the panels, the library) and `-LowEnd` repeats it on
  an imitation of a weak computer. The performance suite also measures every visualisation, every
  picture effect on the graphics card, Subject Lock and a library of 20 000 files.

### Changed

- Playing comes first: the threads that read, decode and play run above ordinary priority, so other
  work on a busy computer no longer makes pictures late or sound break.
- The log is written in the background, so the window never waits for the disk to note something.
- Subject Lock converts each picture several times faster, and decoding through Windows no longer
  makes a new copy of every picture and packet for the memory manager to clear away.

## 0.23.0 - 2026-10-14

### Added

- Transcribe eligible library videos quietly in the background, with per-file progress, a pause
  control, retries and folder exclusions. It is off until enabled in Preferences.
- Hear the currently playing file ahead of the playhead, adding subtitles piece by piece and
  showing the part already transcribed on the seek bar. The `rexplay transcribe --from` command
  measures the same progressive speech path without opening the window.
- Keep partial transcripts between sessions and provide controls to forget them.

- **Kinetic captions.** Words come in one at a time as they are spoken, and how each word was
  said decides how it looks: a quiet run of words is small and lower case, a stressed word lands
  big and bold in capitals, a word said with a lift in the voice turns gold and italic, and a
  shout flashes and shakes. A pause clears the words and the next ones start somewhere new.
  Ctrl+Shift+V turns them on; they work with any subtitle file or with a transcript.
- **Captions that stay off faces.** The picture is read ahead of what plays, with Windows' own
  face detector, and words go where they cover no face, no text already in the picture and
  nothing that moves. A word gets a dark plate wherever the picture behind it is too bright or
  busy for white text to read.
- **Seventeen caption looks.** Creator cut, sing-along, typewriter, neon sign, headline, comic,
  minimal, rave, classic with the spoken word lit, glitch, news ticker, ghosts, rubber stamp,
  pills, marker pen, stairs and wire, each its own kind of motion, type and placement, plus a
  custom look of your own (Ctrl+Shift+K moves through them). Whatever the look, every word has
  an outline or a plate keeping it readable over the picture.
- **Transcripts.** rexplayer can hear a film's speech on this computer, word by word with the
  moment each is said, using an open speech model downloaded once on request. The transcript
  becomes a subtitle track, is kept for next time, and can be saved as SRT or WebVTT; with a
  subtitle file as well, its words are put at the moments the transcript heard them.
- **rexplay captions, transcribe and models.** Kinetic captions frame by frame as JSON, the
  speech of a file as a transcript, and the speech models listed, fetched or removed.

### Fixed

- Kinetic captions from a subtitle file and a transcript now take the transcript's timing, so
  each word lands on its syllable; before, the subtitle file's own cue times were used.
- Words in confetti and plated looks no longer overlap: widths follow the typeface and case,
  and each confetti word lands clear of the others.

## 0.22.0 - 2026-10-13

### Fixed

- **The beat of syncopated music.** The beat is found where the kicks fall in time, not how the
  rhythm repeats, so Amapiano and other music with a 3-3-2 bass no longer reads at four-thirds or
  two-thirds of its tempo, and its beats land evenly.
- **Subject Lock removal looks right on real footage.** The removed area is now filled from
  the edge inwards with the texture actually around it (grass continues as grass, a gravel band
  or a skirting board runs straight through), and shading is blended so no seam shows where the
  fill meets the picture. Nothing is ever copied from the subject itself, so shoes and trouser
  legs no longer reappear inside the hole, and background is trusted as seen only once it has
  looked the same for a few frames, so a person walking past is not pasted back in. A head
  above a dark coat, a hand and other parts unlike the scene around the subject are removed
  with it.
- **Removal no longer switches off for good when the subject is lost.** The preview pauses and
  says so, and resumes once the subject is found again and followed steadily.
- **The beat keeps time at any frame rate.** The visualisations' beat is judged against the last
  second and a half of the music however many frames a second the window draws, and the tempo is
  only replaced after nearly half a second of doubt, not after eight frames. On a fast screen the beat
  used to settle on the off-beat of a busy groove.
- Trails, tunnels and smoke in the visualisations last as long and move as fast at sixty frames a
  second as at thirty; they used to fade twice as fast whenever the window drew quickly.
- The silhouette's dimmed room no longer streaks across the bars beside the camera's picture.
- The vinyl record's groove and sound ring follow the Sensitivity setting again.
- The embers' swirl no longer jerks back and forth around each beat.
- The beat meter's beat light shows on small visualisations and goes out when the song changes.
- Deleting the file that is playing no longer leaves it in Continue watching.
- Play next plays next even with shuffle on.
- The Delete key on a watched folder in the library no longer offers to delete every file in it.
- Restoring a library backup no longer brings back files removed from the library.
- The library's delete and remove confirmations read correctly for several files.

### Added

- **Kinetic captions and offline speech transcription.** Words can arrive with the music,
  grow, move and highlight according to the voice, with ten ready-made looks and custom
  controls. Subtitle timing can be aligned to locally generated transcripts; speech models
  are downloaded only when requested. The `rexplay captions`, `transcribe` and `models`
  commands make the same features available from scripts.
- **Five camera visualisations** that put you in the music: Clones (your dance frozen on each
  beat), Time warp (a slit-scan mirror the bass deepens), Aura (a glow with shockwaves on the
  kick), LED wall (you as a spectrum-gated wall of lights) and Stardust (you in motes that the kick
  blasts apart).
- **Seven more camera visualisations:** Hall of mirrors (you folded into mirrors that turn on the
  beat), Light painting (strokes of light wherever you move), Scanner (a bar that sweeps you once a
  beat), Shatter (your picture in tiles the kick blasts apart), Spotlight (stage lights that follow
  you and change on the kick), Infinity mirror (you inside yourself, each level a beat behind) and
  Water (you through ripples each kick drops).
- **Fifteen new visualisations**, each moved by the music its own way: Groove (the last seconds
  scrolling by as spectrum, beat grid and drum lanes), Flight (a tunnel of light rings that pass on
  the beat), Cymatics, Ridgelines, Synthwave, Fireworks, Lightning, Lava lamp, Constellation,
  Galaxy, Kaleidoscope, Murmuration, Ink, Skyline and Mandala.
- **Thirteen more picture effects.** A new Weather & light group: Snow settle piles snow on
  ledges, Thunderstorm flashes lightning through driving rain, Aurora turns the scene to night
  under rippling curtains, Stage lights sweep coloured beams through haze. Tiny planet wraps the
  picture into a little world, Droste repeats it inward forever, Reflection pool turns the lower
  part into water. Page curl, Bubbles and Disintegrate answer the pointer. Hologram, Paper layers
  and Stencil graffiti remake the picture in light, paper and spray paint.
- `rexplay pulse --fps` hears a file at the frame rate asked, to check the beat at any of them.
- **Blend, a second removal mode.** In Subject Lock's Removal preview, Blend covers the
  selection with the surface just around it, as a retouching brush hides a logo or a stain on a
  shirt, instead of showing what is behind it. The choice is remembered.
- **Enhanced Subject Lock works on paused pictures.** With the optional models installed, a
  click on a paused picture outlines the whole object ("Finding the object..."), and the removal
  preview of a paused picture is painted by the inpainting model, with the status saying so.
  Models run in the background, any new picture or click cancels them, and without them, or
  when they are unsure, Subject Lock uses its own methods as before.

### Improved

- **Faster starts and first pictures.** The app warms up storage, decoding, sound and its
  picture surface as the window loads; it reads ahead for the next item, keeps compiled
  shaders available and can show the first available picture sooner after a seek.
- **Subject Lock reacquisition diagnostics.** The search retains the best candidate's
  match evidence, relative resemblance and closest rival for more reliable investigation
  when a lost subject is found again.
- **The object effects, tuned from rendered previews.** Fireflies no longer hide the picture,
  Electric edges arc only on strong outlines, Jigsaw pieces have proper tabs and sockets, and
  Confetti, Bokeh, Melt and Motion outline are easier to see and steadier.

## 0.21.0 - 2026-10-13

### Added

- **Thirteen more picture effects, with objects that react to the picture.** Pinball sends a
  ball bouncing round the frame that squashes on outlines and pushes the picture aside;
  Fireflies gather on highlights; Jigsaw pieces lift under the pointer; Colour magnet pulls
  whatever matches the colour under the pointer; Ink spill floods the dark areas first;
  Confetti is blown aside by the pointer. Electric edges, Time lens and Sonar ping follow the
  pointer too. Motion outline draws marching ants around whatever moves. Bokeh, Heat haze and
  Melt read the picture itself.
- **Thirteen more picture looks.** Film: Technicolor, Silver gelatin, Super 8. Mood: Autumn,
  Candlelight, Overcast, Spring, Dusk. Stylised: Acid, Ultraviolet, Comic, Chrome. Essentials:
  Vivid.

## 0.20.0 - 2026-10-13

### Added

- **Thirteen more picture effects.** Interactive: Ripple touch, Torchlight, Gravity well,
  Scan sweep, Shatter, Parallax, Rain on glass and Zoom burst follow the pointer and fall back
  to the centre or to time when it is away. Motion: Flow field draws the picture's own movement
  as coloured streaks and Motion isolate keeps colour only on what moves. Image-aware: Cel shade
  and Terrain mesh. Mosaics: Low poly. Each has four controls with named modes where they fit.
- **Thirteen more picture looks, now in groups.** Film: Slide film, Faded print, Noir. Mood:
  Moonlight, Desert, Forest, Pastel. Stylised: Cyberpunk, Solarize, Posterize, Duotone,
  Cyanotype. Essentials: Negative. The Picture look menu groups every look as the effects menu
  does, and each look has a one-line description in its settings.

### Fixed

- **Five looks rendered as Night vision.** Teal & orange, Golden film, Bleach bypass, Cross
  process and Infrared had lost their shader code; they draw again.
- **Mosaic effects tuned from rendered previews.** Larger, readable cells for Dice, Cube, LED,
  Cross-stitch and Digital rain; brick and cube colours now match by hue, so foliage comes out
  green rather than grey; Lumen weave, Lens bloom and Stipple engrave are stronger and cleaner.

## 0.19.0 - 2026-10-13

### Added

- **Click to lock picks the right box.** One click on a person, a face or an object now
  selects the box around it that Subject Lock can follow best, judged by how it stands out
  from its surroundings, where its outline runs and how much detail it holds. A click on sky,
  a plain wall or a repeating pattern is refused with a reason instead of locking a patch of
  background, and the chosen box is outlined for a moment so you can see what was selected.

### Improved

- **Subject Lock and cuts.** When the film cuts to another shot, Subject Lock stops following
  whatever is now under the box and searches for the subject in the new shot instead. Camera
  pans and changes of light are not mistaken for cuts.
- **Faster tracking.** Following a visible subject does less repeated work each picture.

## 0.18.0 - 2026-10-12

### Added

- **Episode skip controls.** Dedicated Next episode and Cancel automatic skip actions, markers
  for detected intros and credits on the seek bar, and ways to review, correct or reject a
  proposed boundary.
- **Rebuilt Subject Lock removal.** A subject matte, motion-compensated background plate and
  texture-aware reconstruction remove selected regions while preserving the original source.

### Improved

- **Episode section detection.** Intro and credits analysis now handles cold opens, recaps,
  shortened titles, sparse keyframes, end-credit gags and post-credits scenes more carefully.
  Background analysis cancels safely when a different episode opens.
- **Subject Lock removal refinement.** Mask tolerance, edge growth and feathering are adjustable;
  previews retain the observed background without treating a repeated frame as new evidence.

- **Contour effects find subtle outlines.** Neon contours, Ink trace, Chromatic contours and
  the three Ghostwire styles now measure edges against the picture's own noise, at three scales
  and in colour as well as brightness. Faint outlines in dark, soft or low-colour scenes show,
  while grain, compression noise and banding no longer turn into specks, and lines hold still
  from one picture to the next. Black bars recorded into a video no longer draw a line.
- **Each contour effect has its own look.** Neon contours turns the main outlines into pink
  neon tubes; Ink trace sketches tapering strokes and cross-hatched shadows on paper;
  Topographic contours draws a tinted, hill-shaded relief map of the picture's brightness;
  Chromatic contours draws every fine line in the colour of its direction on black; Ghostwire
  mask mode shows flat violet shapes with solid outlines and dotted inner contours; Ghostwire
  motion's surface view is a silver relief.
- **Kaleidoscope, Oil paint and Tape wear look better out of the box.** The kaleidoscope repeats
  the middle of the picture, oil paint uses broader strokes, and tape wear wobbles visibly.
- **Effect settings explain themselves.** Every effect describes what it does, every control
  has a tooltip, the picker in Preferences shows each effect's group, and labels say what the
  controls really change (for example Cursor lens has Lens size, Kaleidoscope has Mirror zoom).
  Customizing with no effect on says so instead of showing empty controls.

### Fixed

- Kaleidoscope's Mirror folds and Rotation speed controls now work.
- Effects with four controls start with their saved settings after the player restarts
  (Thermal map no longer starts flat grey, Pixel sort and Motion smear no longer start off).
- Warping effects (Kaleidoscope, Vortex, Liquid glass, Prism flow and others) mirror the
  picture at its border instead of smearing the edge pixels into streaks, and nothing reads a
  decoder's padding at the picture's edge.
- Waves and swirls are the same size across as down on every picture shape; Vortex keeps the
  edges of the picture still.
- Halftone print leaves white paper clean and closes full ink up solid.
- Glyph mosaic's screen palettes light the bright parts of the picture, not the dark ones.
- Thermal map's Rainbow runs from cold blue to hot red instead of giving black and white the
  same colour.
- Pixel sort's streaks run in the direction named, and reach far enough to see.
- Tape wear wobbles in bands, bleeds colour rather than just red, and has a real
  head-switching band.
- Oil paint's Edge sharpness control now changes the strokes.
- Reset puts an effect's intensity back to its own default.
- Effects that compare with the previous picture keep it only while they need it (not at
  zero strength, nor with Motion smear's amount at 0).

## 0.17.0 - 2026-10-12

### Added

- **Lumen weave.** A new contour effect that follows each outline along its own direction
  and keeps only edges that stay continuous, so noise drops away and real silhouettes
  become flowing strands of light. Strands are coloured by how sharply they curve, or by
  source colour, monochrome or brightness, and Strand flow sends light travelling along them.
- **Five more picture effects.** Depth haze fades soft and flat areas into a coloured
  atmosphere; Particle veil sends sparks up from whatever is moving; Lens bloom adds
  anamorphic streaks and glow to highlights; Stipple engrave draws the picture as an
  engraving whose lines bend with the edges; Time echo splits movement into a red echo
  behind and a blue echo ahead. Each has its own saved controls and named colour choices.
- **Five more picture looks.** Teal & orange, Golden film, Bleach bypass, Cross process
  and Infrared.

### Improved

- **Faster start for video.** The picture shaders compile once per session instead of for
  every video surface.

## 0.16.0 - 2026-10-12

### Added

- **Updates without the waiting.** rexplayer looks for a new version by itself every day,
  downloads it quietly in the background and checks it against its published checksum.
  An update bar then offers Restart now (you come back paused at the same place), Install
  when I close rexplayer, Install tonight, Remind me later or Skip this version, with what's
  new. It never appears over full-screen playback.
- **Choose how updates install.** Preferences, Updates: install by itself when rexplayer is
  not in use, download and ask, or only tell me. Preferences also shows the update's
  progress and what the last update brought. A cancelled install reopens rexplayer where
  you were.

### Improved

- **Library.** The selection survives sorting, filtering and scans; an opened show, season
  or album never shows stale items; reversed orders keep ties in order; Open containing
  folder works for every selected item.
- **Keyboard.** Arrow, Enter, Delete and selection keys work inside a focused library or
  playlist instead of seeking or changing the volume, while Space still plays and pauses.
  Escape closes the Subject Lock panel, and input inside the panel no longer moves the
  picture.
- **Episode sections.** An unreadable episode no longer stops analysis of the others, and
  duplicate copies of one episode no longer count as separate evidence.
- **Visualisations and effects.** Adaptive quality now works at every window size, and an
  effect at zero strength stops keeping a copy of the previous frame.

## 0.15.11 - 2026-10-12

### Added

- **One-click episode skipping.** Detects likely opening and closing sections using
  repeated audio landmarks across episodes, credit-frame evidence and explicitly
  named chapter boundaries. Confident suggestions show **Skip intro** or **Skip credits**
  immediately, without requiring a confirmation first.
- **Episode section inspection.** The `rexplay sections` command reports detected
  segments and supporting evidence, including a machine-readable JSON form.
- **Library controls.** Sort and filter library views by progress, media type,
  folder and additional ordering choices, with saved view preferences.

### Improved

- **Playback while browsing.** Preferences control whether opening the library
  pauses video and whether audio-only media may keep playing.
- **Library maintenance.** Better handling of deleted items, stale references,
  folder removal and library scanning.
- **Reliable playback lifecycle.** A paused media restore announces its duration
  to the seek bar; the window ignores late playback events while closing.

### Safety and limitations

- Detection only offers a button when confidence is high enough; automatic seeking
  remains **off by default** for intros and credits, with separate opt-in switches.
  Manually saved section bounds take precedence, and incorrect detections can be rejected.
- Repeated opening and closing boundaries have not yet been measured against a
  representative, annotated collection of real episodes.

## 0.15.10 - 2026-10-12

### Improved

- **Subject Lock recognises a returning subject after the light changes.** Shape
  is compared independently of brightness and contrast, and colour is compared
  as chromaticity, allowing for a change of light measured across the whole
  picture. A coloured reflection or a different lamp no longer rejects the
  subject, while one candidate still cannot explain its own colours away.
- **Relative acceptance.** A returning subject must look more like remembered
  views of itself than like remembered views of the rest of the video, beat
  every rival elsewhere in the picture by a margin, and be seen twice.
- **Pose, turning and size.** Remembered views cover new viewpoints, recovery
  settles each candidate onto the subject's outline, and local tracking no
  longer lets the box collapse onto part of a subject in uniform clothing.
- **Faster picture conversion.** Colour conversion resolves each plane once per
  row instead of three times per pixel, which shortens the Subject Lock
  readback path.

### Fixed

- Switching Ghostwire Motion off releases its previous-frame texture, and its
  shader no longer reads the previous frame in modes that do not use it.
- Resetting a picture-style preset no longer writes back the keys it removed.
- The Subject Lock panel describes auto-follow as it actually is, and a manual
  zoom or pan announces that it switched auto-follow off.

### Testing

- Adds Windows regressions for self-similar clothing, loose selections,
  coloured light, turned-round and deformed returns, colour-only false matches
  and gradual stretching. The full local check passed before merge; this
  release was merged without waiting for CI at the owner's request.

### Limitations

- Subject Lock compares visible surfaces, not identity. Two people dressed
  alike are reported as ambiguous. Accuracy has not yet been measured against
  an annotated benchmark.

## 0.15.9 - 2026-10-11

### Improved

- **Subject Lock playback responsiveness.** On-screen recovery now searches an
  owned video snapshot on a background worker instead of repeatedly searching the
  entire picture during presentation. While a search is running, the presenter
  can avoid redundant picture readbacks; local tracking also uses a bounded
  coarse-to-fine search.
- **Return after an exit.** Recovery uses less descriptor sampling work and no
  longer treats a change in the subject's background contrast by itself as a
  reason to reject an otherwise plausible appearance match. Confirmation and
  competing-match checks remain in place.
- **Recovery lifecycle.** Seek, reset and reselection cancel outstanding searches.
  A background result is checked against the current picture before a lock is
  restored, reducing the chance of applying an outdated result.

### Testing

- Adds Windows regression coverage for background recovery, worker lifecycle,
  target appearance and candidate validation.
- Stabilizes timer playback tests with a cancellable audio-write barrier and
  expands selected container and playlist regressions.
- The preceding application commit passed the Windows build, adapter/coverage
  and package gates, desktop UI Automation, performance, portable Linux and
  website CI. The version-only release change must pass its own checks.

### Limitations

- Appearance matching cannot guarantee identity through arbitrary rotations,
  occlusions or similar-looking subjects. The flower-dress and grey-outfit
  footage still need an annotated, repeatable accuracy and frame-time benchmark
  before claiming reliable recovery under all real-world conditions.

## 0.15.8 - 2026-10-11

### Improved

- **Subject Lock distance changes.** Tracks a selected patch with a size-adaptive local
  search, rather than leaving the rectangle fixed as a subject moves nearer or farther.
- **Return after an exit.** Searches additional subject sizes and refines intermediate
  dimensions and placement. A coarse spatial-colour descriptor supplies an alternative
  cue for moderate viewpoint and lighting changes, while the original visual reference
  remains the basis for recovery.
- **Lighting tolerance.** Models bounded brightness and contrast changes during
  recovery, while retaining repeat-sighting and competing-match checks.

### Testing

- Adds Windows regressions for continuous changes in apparent size and return sizes
  that fall between the previous discrete search levels.

### Limitations

- Visible colour and texture are not semantic identity. Similar clothing, drastic
  viewpoints, motion blur and extended occlusion can still cause a lost lock or a
  mistaken match. Real-footage precision, false-lock rate and recovery latency are
  not yet established by an annotated benchmark.

## 0.15.7 - 2026-10-11

### Fixed

- **Subject selection on still pictures.** Refreshes the decoded picture after a
  new Subject Lock selection, including when a slideshow picture is already
  visible or has reached its end. The presenter receives the selection before
  the image is decoded again, so tracking can inspect the chosen region.
- **Playback generation safety.** Carries the active seek generation through to
  video frames presented after decoding, helping prevent stale frames from
  crossing seeks into tracking and picture effects.
- **Upgrade hand-off safety.** Consumes one-time recovery markers before
  attempting restoration and rejects invalid queued items, so an interrupted
  restore is not replayed at a later launch.
- **Picture caching and presentation.** Hardens thumbnail caching and the
  unedited-frame path used by Subject Lock previews.

### Improved

- **Tracking reliability.** Tightens bounded reacquisition and conservative
  subject-identity checks without treating an uncertain match as a confirmed
  return.
- **Regression diagnostics.** Expands engine, Windows presenter and desktop
  coverage. UI Automation now includes the visible Subject Lock status and app
  log context when selection times out.

### Limitations

- Subject Lock follows visible texture, not semantic identity. Occlusions,
  lookalikes and low-detail regions may still require manual reselection.

## 0.15.6 - 2026-10-10

### Fixed

- **Unreliable initial selections.** When an area has too little distinguishing
  texture, Subject Lock now waits for an explicit new selection instead of possibly
  locking onto an unrelated object that later enters the rejected coordinates.
- **Oversized video safety.** Frames exceeding the local tracking budget clear the
  previous subject and removal preview rather than retaining stale tracking state.
  Manual reselection also starts with fresh frame dimensions and reference data.

### Improved

- **Tracking maintainability.** Moved full-picture re-detection into its own source
  file, separate from per-frame tracking and non-destructive preview handling.
- **Regression coverage and audit.** Added Windows tests for rejected selections,
  frame-budget limits, resolution changes, manual reselection and invalid pixel
  formats. Published a detailed Subject Lock reliability audit and test matrix in
  `docs/subject-lock-reliability-audit.md`. The audit PR passed the Windows adapter,
  desktop UI, packaging/update, performance, Linux and website CI jobs.

### Limitations

- Visible-texture matching cannot guarantee recovery through all pose changes,
  occlusions, motion blur or lookalikes. A benchmark on representative, annotated
  real footage is still needed to quantify false locks and reacquisition latency.

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

Versions before 0.10.0 are in [docs/changelog-archive.md](docs/changelog-archive.md).
