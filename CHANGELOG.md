# Changelog

Every version of rexplayer that reaches users, newest first. CI publishes a version when its number reaches `main`.

## Unreleased

### Improved

- The Fastest performance mode decodes page banner artwork at up to 960 pixels across (about a third of the memory of the full 1600), and Preferences says so.
- When the window's animations are off (Fastest, or Windows' own animation setting), the playing song's equaliser mark stands still instead of animating, while still marking the line.
- The automation pipe's `status` reports `picturesPending`, the picture requests still queued or loading, so a script can wait for the pictures on screen to settle. The Windows screenshot tests wait on it, so a card's placeholder is never captured in place of its picture.
- The library's navigation list no longer flashes grey placeholder boxes before its entries draw.
- **Library artwork gives its memory back.** Decoded pictures are kept within a byte budget set by the performance mode (smaller on Fastest and while Energy Saver is on) instead of a flat 400, changing view lets most of them go, and closing the library keeps only a few.
- **Preferences opens and cancels quickly.** Only the section shown is built; the others are built the first time they are chosen or searched, and Save reads only the sections that were opened.

### Fixed

- **The Mac app builds again, so new versions can publish.** Speech recognition's Linux and Windows libraries were being packed into rexplayer.app, where the Mac's tools refused them and stopped every build since 0.31.0; only the Mac's own libraries go in now.

## 0.31.0 - 2026-10-25
### Added
- **Native macOS playback and library parity (#555).** Expanded AppKit controls for media information, bookmarks, the queue and named playlists, library folders and backup/restore, context commands, configurable shortcuts, precise audio and subtitle sync, overlays, video sizing, local pictures and first-run guidance.
- **Mac music presentation.** Cover art and source-timed lyrics, real sound-reactive visualisation scenes with a native mode picker and effect controls, and playback speeds from 0.25× to 4× with pitch-preserving WSOLA or an optional pitch-shifting mode.
- **Offline Mac speech and captions.** Download verified speech models on request, transcribe played or library media locally, export timed captions, and animate source-timed words with selectable subtitle/transcript sources, layouts, palettes and accents.
- **Mac snapshots and playback tools.** Export source-resolution PNG, JPEG or BMP stills with configurable filenames and collision-safe sequence numbers; use nonblocking UI for file actions, captions, timings and confirmations.

### Improved
- **VideoToolbox decoding preferences.** Global and per-codec H.264/HEVC hardware decoding choices; display the configured policy and decoder source in playback statistics.
- **Subtitle styling and accessibility.** Honor ASS/SSA font and style settings when requested, improve bidi shaping and cue placement, and give native controls automation identifiers, accessible labels and help text.
- **Safer updates and recovery.** Check and verify universal Mac archives with resume support; defer or skip offered versions; preserve playback state across unexpected interruptions; improve native keyboard traversal and system media controls.
- **Repository contracts.** Update the macOS capability ledger and its generated matrix without treating implemented-but-untested work as verified. Correct the cross-platform Speech project graph and the Mac smoke-test formatter.

### Known limitations
- Some macOS capabilities remain only **built**, not verified against native Mac integration and accessibility tests. The native Opus decoder, camera visualisations, complete updater installation handoff, and decoder fallback behavior still require work. See `docs/parity.md`.
- Packaging and publication require passing CI; infrastructure failures downloading the pinned .NET SDK can prevent the release workflow from finishing.

## 0.30.9 - 2026-10-25
### Added
- **Lyrics on request.** Find lyrics for the current song from LRCLIB when no local lyrics are present. Match title, artist and duration before showing them; keep local `.lrc` files and embedded lyrics first. The lookup sends song details only when Find lyrics online is pressed.
- Save displayed lyrics as an `.lrc` file, and export a timed speech transcript as `.lrc` alongside SRT, WebVTT and JSON. Songs are no longer offered for transcription; audiobook transcription remains available.

### Improved
- Find Windows Preferences by category, setting label or choice, with a persistent category list on wide windows and readable no-results feedback (#467). Reject blank and nearly uniform UI screenshots before accepting visual baselines.

## 0.30.8 - 2026-10-24
### Added
- **Native Mac library browsing.** A local-first AppKit library window lists watched folders, songs, videos and pictures, with search, safe pagination, Play and Add to Up Next. Add and stop watching folders without deleting the original media; rescan available folders periodically.
- **Mac Up Next interactions.** Browse, reorder, shuffle and remove queue entries in the native window, including recovery when the last queue page becomes empty. Queue and library windows expose descriptive names to assistive technology.
- **More useful local Home shelves.** Pin real music albums, browse matching movies and shows, continue listening and watching, and return to the correct library view after narrowing a search or filter.

### Improved
- **Music and lyrics.** Distinguish missing, plain, line-timed, partly word-timed and word-timed local lyrics. Seek by a source-provided line time, keep manual scrolling under the user's control and use readable system colors and word-weight emphasis in high contrast.
- **Adaptive Windows library.** Keep useful Home content width in compact windows, restore desktop navigation when widened, explain disconnected folders and offer an immediate scan retry. Movie and TV Browse respect ordinary saved sort order on their All items shelves.
- **Movie and show discovery.** Present accessible feature actions and native detail pages without mixing up Play and More info. High-contrast settings replace photographic hero backgrounds with system colors and suppress decorative fades.
- **Visualisation and app responsiveness.** Rebuild visualisation backing pixels when display scale changes; preserve the chosen mode through pause, seek, track changes, resizing and window minimization.

### Fixed
- **Featured media controls.** Clicking Play or More info on a movie/TV feature no longer also activates its parent card.
- **Empty library recovery.** Clearing a search or filter returns keyboard focus to the surviving control, while a disconnected folder is explained instead of being mistaken for an empty library.
- **Mac queue page removal.** Deleting items from the final queue page returns to a valid remaining page.
- **Website previews and image provenance.** Pre-publication checks catch broken or undersized PNG assets; any future finalized native release screenshot must carry matching platform, source-version, reviewer and SHA-256 evidence rather than being relabeled from a test fixture. Responsive browser checks verify decoded screenshots and narrow-width layout. The Pages workflow also checks the actual deployed commit and retains browser captures after publication.

## 0.30.7 - 2026-10-23
### Fixed
- **Mac playback tools and release builds (#460).** Use the saved bookmark time field and connect the new Playback tools window to the Mac application menu, automatic state refresh and cleanup, restoring the Intel and Apple Silicon builds.
- **Warnings-as-errors compliance (#460).** Remove an unfinished, unused native media-list prototype rather than suppressing the unused-type analyzer; existing playback functionality stays intact.

### Improved
- Refresh the Windows library with soft light and dark surfaces, clearer navigation, a persistent Add folder action, and a dedicated search row. View and library actions scroll at smaller window sizes.
- Make playback easier to discover with a prominent play/pause button, transport hints, and direct welcome-screen actions. Separate the macOS timeline from its playback controls and add file/folder welcome actions.
- Offer a Clear search action when no media matches, and make empty-library folder suggestions fit narrow windows.

## 0.30.6 - 2026-10-23
### Fixed
- **Season 0 detection (#458).** Recognise S00, Season 00 and Specials folders with quality labels, including the Simpsons S00-S09 collection and its original shorts. Keep unnumbered clips and bonus folders with their show rather than losing them to Movies or separate series.
- **TV season selection (#458).** Show “Season 0 (Specials)” explicitly in the season picker; preserve numbered-season playback and the usual Specials grouping.

## 0.30.5 - 2026-10-23
### Added
- **Audiobook controls (#455).** Audiobooks have their own transcription preference and default visualisation, independent of songs and other audio. Camera-based defaults still require consent and classification changes take effect with the item that is actually playing.
- **Browse throughout the media library (#455).** Songs, albums, artists, genres, audiobooks, authors, podcasts, recordings, videos, pictures and recent views can use horizontal Browse shelves as well as their existing layouts.
- **Completed media (#455).** A dedicated library view gathers finished audiobooks, watched movies and episodes, played podcast episodes, music and recordings, while keeping anything with a resume point in progress.

### Improved
- **Media parsing against real libraries (#455).** Recognise dotted and bracketed audiobook collections, author/title/part filenames, audiobook genre variants and season-labelled TV specials more reliably, while keeping albums together when only some chapters carry useful tags.
- **Richer discovery (#455).** Fill missing genre groupings from fetched album metadata and search audiobook credits, movie and show genres, cast, directors, writers and creators in addition to local tags.
- **Collapsed library navigation (#455).** Use a purpose-built centred icon rail with consistent touch targets, selection styling, tooltips and accessible names instead of clipping the expanded sidebar.

### Fixed
- **Audiobook shelf leaks (#455).** Keep audiobook intros and chapters, including the Chronicles of Narnia, Roald Dahl collections and spoken-word releases tagged as non-fiction audiobooks, out of Songs and genre shelves.
- **Library scrolling stalls (#455).** In Balanced and Fastest modes, background transcription and intro or credit analysis yield while browsing so artwork and navigation stay responsive. Best quality leaves that background work running; Automatic follows the selected tier.

## 0.30.4 - 2026-10-23
### Fixed
- **Mac launch on Sequoia (#454).** Ad-hoc-signed Intel, Apple Silicon and universal app bundles include the .NET JIT entitlement needed to prevent a runtime abort before the window opens.
- **Mac bundle validation (#454).** Parse the actual code-signing entitlements separately from diagnostic output, retaining strict checks for signed entitlements, supported architectures, minimum macOS version and package layout.
- **Mac media information (#454).** Restore codec display names in the Mac media-information window.

### Improved
- **Sequoia launch verification (#454).** The final signed universal download is launched and tested for video playback and clean shutdown on a macOS 15 CI runner, in addition to per-architecture build checks.
- **Finder integration and parity tracking (#454).** Expand supported Open With file associations, clarify actual Mac feature coverage and update the generated parity and capability documents without claiming unfinished features are complete.

## 0.30.3 - 2026-10-22
### Improved
- **Mac compatibility.** The Mac app now ships as one universal download for Intel and Apple Silicon, explicitly supports macOS 12 and later, and verifies every release bundle against macOS Sequoia 15.8.1 before publishing.
- **The right download for each computer.** The website detects Windows and macOS, offers the matching current release automatically, and keeps the other supported downloads available when needed.

## 0.30.2 - 2026-10-22
### Improved
- **Audiobook writer credits (#445).** Prefer dedicated author tags over performer or narrator credits, recognize explicit written-by bylines and author/book folders, and carry a single confirmed writer across a book's untagged chapters.
- **Conservative book matching (#445).** Use an exact-title catalogue match to fill in a missing author only when a complete Open Library response confirms one work and one writer; keep ambiguous books under Unknown author and cache credited book information across restarts.

### Fixed
- **Disc and part folders (#446).** Keep book titles intact and avoid mistaking disc or chapter folders for writers, while keeping conflicting author credits separate.

## 0.30.1 - 2026-10-22
### Improved
- **A calmer library during downloads.** Bursts of file changes are batched so large download queues do not repeatedly rebuild the library. New, renamed and removed files still appear, while growing downloads keep their progress and become playable as usable data arrives.
- **Reversible library navigation.** The sidebar expands with one click even in a narrow window with a saved collapsed preference. Widening the window restores the desktop preference without losing your place.
- **Minimal interface controls stay within reach.** A visible Restore full interface button makes minimal mode easy to leave. Library and fullscreen controls remain available, compact volume controls fit beside playback, and long titles are bounded so they cannot push essential actions out of view.

## 0.30.0 - 2026-10-22
### Added
- **Subject tracking (#419).** Improved selected-subject tracking and its controls, making it easier to follow the intended subject across video frames.
- **Featured artists (#436).** Songs with multiple or featured performers now credit and group their artists more accurately, including featured appearances on artist pages.
- **Audiobook recognition (#437).** Better separation of audiobooks, podcasts, recordings and music, with audiobook-oriented library browsing.
- **Collapsible library groups (#438).** Sidebar groups can be collapsed for a tidier library while keeping available collections easy to find.
- **Album, genre and movie parsing (#439).** Improved recognition and grouping of albums, genres and movie titles from local metadata and filenames.
- **Authors, book descriptions and artist profiles (#441).** Audiobook Authors navigation leads from author to book to chapters, including an Unknown author fallback. Conservatively matched Open Library book synopses show attribution and a source link. Music artists can display real Wikimedia Commons portraits and credited biographies.

### Changed
- **Minimal UI and movie details (#440).** Refined the library's visual presentation and made movie information and navigation more useful.

### Fixed
- **Edited transcript preservation (#421).** Preserve user-edited transcript content instead of overwriting those edits during subsequent processing.

## 0.29.0 - 2026-10-22
### Added
- **A library that always gives you a way back.** Movie, show, season, episode, artist and album pages have a persistent, labelled Back button and Alt+Left support. Back restores the exact source, search, selection, focus and scroll position that opened the page instead of rebuilding a similar-looking view somewhere else. Browse keeps Sort and Filter available, so changing what is shown never requires switching layouts.
- **An immersive library rail.** The sidebar can collapse to a 64-pixel icon rail, remembers the choice and compacts automatically in narrow windows. Every icon keeps its name in a tooltip and to assistive technology, while Browse and details expand into the reclaimed space.
- **A varied Browse showcase.** Movies and TV Shows curate five distinct features from the library and rotate them slowly with a restrained fade. Rotation pauses during interaction, stops while hidden or outside Browse and becomes an immediate swap when animations are disabled. The billboard opens rich details; its separate Play or Resume action names the actual next episode when there is one. It never starts a trailer, network request or video by itself.

### Changed

- **The player now looks and reads like the medium playing.** Music, podcasts, audiobooks, recordings, other audio, pictures, episodes and videos receive their own label and useful title, creator, show, album, year, genre and length hierarchy. Artwork has more presence, the bottom transport carries a compact second line of context, and the seek target and control grouping are calmer and easier to use.

### Fixed

- **Playable downloads start before the download ends.** rexplayer re-probes a growing file as bytes arrive, opens it once its headers and first required ranges are usable, and buffers only when playback reaches bytes that are not present yet. The buffering card shows determinate progress when the expected size is known, retries through quiet download intervals, resumes automatically, cancels cleanly on a seek and still finishes normally when a changed source settles at its end.
- **An empty playlist explains what to do.** The playlist panel now says that media can be opened or dropped there instead of showing a blank surface.

## 0.28.0 - 2026-10-21

### Added

- **Choose between local movie resolutions.** Releases like `[SPVT] Iron.Lung.2026.1080p` and `Iron.Lung.2026.2160p` now resolve to the same film rather than making separate catalogue lookups. When the library holds different encodings of one film, a dropdown beside the seek bar lists their resolutions and exact filenames; switching keeps the time, paused state and playlist. Different years, short clips and noticeably longer cuts do not join automatically.
- **Optional cleanup of generated SRT copies.** Transcription preferences can remove rexplayer's model-labelled original and edited SRT files beside known media when sidecar saving is turned off, or clean them manually. Other subtitle files and saved transcript data stay in place.
- **Audiobooks, Podcasts and Recordings have views of their own.** Each appears in the library's sidebar, after the music views, only while it has something in it, and goes again when its last file is moved, deleted or its folder unwatched; a view that empties while open returns to Home and says so. During a scan the sidebar waits for the scan to settle, so views never blink in and out. Audiobooks shows one card per book (its parts, "Disc 2" folders and "Part 3 of 12" files gathered by album, folder and author) with how far it is and "Continue listening" first; a book's page lists its chapters (an .m4b's chapter marks, else one a part), Play goes on from where the book was left, each book keeps its own speed, and "Mark as finished" or "Mark as unfinished" is a right-click away. Podcasts shows one card per show (album, artist or folder, the same show in two folders as one) with its newest episode's date and how many are unplayed; a show's page lists its episodes newest first with their played state, and offers "Play unplayed in order" and "Mark all played". Recordings lists voice memos and recorders' files by day (Today, Yesterday, This week, then each month), WhatsApp voice notes under their own heading, and short sounds, ringtones and effects at the foot. Home gains "Continue listening" and "New episodes" shelves when there is something for them. Right-click any audio file for "Move to Music", "Move to Audiobooks", "Move to Podcasts" or "Move to Recordings"; the move is kept like your other edits, so rescans and clearing fetched metadata never undo it. The rules are in docs/library-classification.md.
- **The details rexplayer fetches, on screen.** The Movies and TV shows billboard shows a two- or three-line synopsis, a facts line (year, length, rating, genres) and Play and More info, over the film's backdrop when it has one. More info, a movie card's info button or its right-click Details open the movie's own page: its backdrop and poster, facts, synopsis, directors and writers, a row of its cast with photos and characters, Watch trailer, the other films of its series in the library and More like this, with Play or Resume; Back returns to exactly where you were. A show's page shows its synopsis and facts (network, years, status) under its title, its cast, each season's overview and each episode's synopsis (two lines; click to read it all). Clicking anyone in a cast row shows everything in the library they appear in. Synopses from Wikipedia carry "Description from Wikipedia" linking to the article, and TVmaze's data a "Data from TVmaze" line. Anything missing is left out, and with metadata turned off every page still reads complete from your files alone. Photos and backdrops load only when shown, at the size shown.
- **Mac sound for MP4, MOV and MKV videos.** On a Mac, AAC (the sound of most videos) decodes through the Mac's own AudioToolbox, held to the same reference as Windows' decoder.
- **Every release now includes the Mac app.** Each version on the releases page carries rexplayer for Apple Silicon and for Intel Macs beside the Windows installer, with checksums; a version that does not build for a Mac does not publish.
- **Videos show their pictures on a Mac.** rexplayer.app draws with Metal, with the same colours and the same letterboxing as Windows; picture looks and effects follow.
- **rexplayer.app, the first Mac window.** A native Mac window plays music and the sound of videos with the same menus, shortcuts (Cmd where Windows says Ctrl), settings, queue and history as on Windows; pictures arrive next. Every change builds it for Apple Silicon and Intel Macs.
- **Mac video through VideoToolbox.** On a Mac, H.264 and HEVC (including 10-bit) decode through the Mac's own VideoToolbox, picture for picture identical to Windows' decode of the same file.
- **A streaming-style music Home, album page and Songs table.** A new Music view opens on a hero for the album or artist you play most lately, tinted from its artwork, with Play, Shuffle and a way to its page, over rows of Jump back in (albums and playlists), On repeat, Recently added, Made for you (an artist mix for each top artist, a genre mix for each top genre and a decade mix, all made on this computer from your own library), albums you haven't played in a while and artists new to your library. Albums, mixes and playlists sit on square cards and artists in round portraits; cards lift when pointed at or focused and their play button plays without opening. An album's page has a larger cover over a backdrop tinted from it, each artist a link, the release's kind (Album, EP, Single or Compilation), year, songs and length, a "..." menu beside Play, Shuffle and Add, numbered tracks with featured artists and a subtle bar of plays, disc headings for a set, the release date, phonographic line and genres when music lookups found them, and more by the same artist. The Songs list is a table: small art, the title, the artist and album as links to their pages, plays and length (right-aligned, in even-width figures), under headings that line up with their columns; the playing song's title takes the accent colour and a small equaliser plays over its dimmed art (still while paused), and Play shows over the art when pointed at. An album's tracks match it, with the equaliser in place of the playing track's number. Long mixes and sets never rank in On repeat.
- **Music lookups fetch artists and release facts.** With the Music switch on, each album also gets its release type (Album, EP, Single, Compilation), first release date, labels and genres from MusicBrainz, and each artist their type, origin, years active, genres and a group's members. An artist's photo is the freely licensed picture their Wikidata item names on Wikimedia Commons, and their bio is the English Wikipedia summary, credited as such. Nothing needs a key, every service keeps its own pace, folder exclusions still apply, and albums settled by an earlier version are looked up once more for the facts they lack (a cover fetched before is kept, and albums with their own art still never get a fetched cover).

### Changed

- **The library lists only the views that have something in them.** Home is always there, and an empty library shows its welcome alone; Songs, Artists, Albums (when a song has an album), Genres (when a song has a genre), Movies, TV Shows, All videos, Pictures, Continue watching, Recently played, Playlists (once you keep one) and Folders each appear when there is something for them, in the same order as before. During a scan the sidebar waits for the scan to settle, so nothing blinks; a view that empties while open returns to Home and says so, and a saved place in a view that has gone opens on Home.
- **Songs, Albums, Artists and Genres hold only music.** An audio file is music when you say so, when the music lookups match its album, or when it has music tags (an artist with a title or album, or a track number; a file named "Artist - Title" counts) and is not marked as an audiobook, podcast or recording by its format, genre, tags, folder or name. Long tagged tracks such as DJ mixes and live sets stay music but no longer count towards "Your most played" or an artist's top songs. Music read before this change is read again in the background, after any new files, to pick up its chapters and podcast marks.
- **Movies holds only films; everything else goes to Other videos.** A video is a movie when you say so, when the movie lookups match it to a film, or, with lookups off or not yet run, when a release-style name or folder ("Title (2007)", "Title.2007.1080p.BluRay") or a title tag with a year comes with a running time of at least 40 minutes. Camera and phone clips, screen recordings, music videos, clips under 40 minutes and anything the lookups could not match go to a new Other videos section, a plain grid of thumbnails beside TV shows, with its own Home shelf when there are some. All videos still lists every video. Films arrive in Movies as their matches come in, without the view losing its place. Right-click any video for "This is a movie" or "Not a movie"; the choice is kept like your other edits, so rescans and clearing fetched metadata never undo it. A film in Movies the lookups could not match says "Not matched" and offers "Find the film..." to search Wikidata by hand.

### Improved

- **How to install rexplayer on a Mac.** A new guide, docs/mac.md, says which download fits which Mac, how to open an app not yet signed by Apple on each macOS version, where its files live, and how an older Mac can reach macOS 12.
- **The parity ledger counts what already works on a Mac.** Thirty rows (playback, gapless, speed, the sleep timer, every container, the audio decoders rexplayer writes itself, playlists and tags) are now verified on macOS, proved by the same tests on the Mac runner as on Windows.
- **Songs by several artists show under each of them.** "beabadoobee/The Marías" or "A$AP Rocky/Brent Faiyaz" no longer appear as artists of their own: the song is counted once under every artist it names, as are artists named with feat., ft., featuring or " x ", those a title features ("Song (feat. Asake)"), and "&", "and" or "," credits whose artists each have songs of their own, while names such as AC/DC, Tyler, The Creator and Earth, Wind & Fire stay whole. Spellings that differ only in case, accents or punctuation are one artist, every value of a multi-artist tag is read (with ARTISTS lists in FLAC, Ogg, MP3 and MP4 files, read again in the background once), an artist page's top songs take in the songs they are featured on and a Featured on section lists other artists' albums they are on, and a song's artist line lists its artists ("beabadoobee, The Marías").

### Fixed

- **Collage shows its pictures.** A show page, or any view, laid out as Collage stayed on grey tiles: its tiles never asked for their pictures. Every tile on screen now gets one, first, and a tile scrolled away lets its request go. Until a picture comes, an episode's tile shows its number on a tint of its show's own over the show's poster blurred, then the picture fades in under a scrim that keeps the title readable.
- **Every show and movie has a poster.** A show with no art of its own gets, in turn, art in its folder or its season's folder (poster, folder, show, cover), the catalogue's poster, its season's art, then a strong frame of its first episode cut to a poster's shape. A movie without a poster gets one cut from a frame the same way, and a show's page wears its poster.
- **Episode pictures show the episode.** An episode shows art beside it, then the catalogue's still of it (fetched once with TV enrichment on), then a frame from well inside it, past the intro and before the credits, skipping black, blown-out and blank frames, so a season no longer shows the same title card on every tile. A thumbnail cut free of recorded bars also loses their last lines, which the picture had tinted, so no dark band runs along a card's edge.
- **rexplayer opens where you can see it.** A window left on a screen since unplugged, or saved larger than the screen there is now, opens on the main screen instead of out of sight; a launch given a blank argument (as some shortcuts pass) opens its window instead of quitting; and a speech model download whose connection stops answering says so.
- **Specials and extras join their show.** A special such as `Futurama S07X01 Simpsorama` no longer appears as a one-episode show of its own: specials marked `S07X01`, `S07 SP01`, `S07 Special 1`, `S07E00`, `S07.5`, `S00E05`, `SP01`, `Special`, `OVA` or `OAD`, and videos in Extras, Featurettes or Behind the Scenes folders, go in the show's Specials, labelled "S07 Special 1" ("Season 7, special 1"). Show names that differ only in case, accents or punctuation are one show, a lone episode named after a show and a season mark joins that show, and a movie's extras stay with the movies.
- **An odd answer from a catalogue no longer stops metadata lookups.** A show, episode or album whose numbers are not whole ones, or whose poster address is not one, used to stop every lookup until rexplayer restarted; now only that detail is left out. A cover offered in Replace cover... that is not a picture is passed over, and a metadata folder rexplayer may not write to is tried again later.
- **Specials and extras join their show.** A special such as `Futurama S07X01 Simpsorama` no longer appears as a one-episode show of its own: specials marked `S07X01`, `S07 SP01`, `S07 Special 1`, `S07E00`, `S07.5`, `S00E05`, `SP01`, `Special`, `OVA` or `OAD`, and videos in Extras, Featurettes or Behind the Scenes folders, go in the show's Specials, labelled "S07 Special 1" ("Season 7, special 1"). Show names that differ only in case, accents or punctuation are one show, a lone episode named after a show and a season mark joins that show, and a movie's extras stay with the movies.
- **A full disk or a locked file no longer stops rexplayer.** Resume points, bookmarks and the library are kept in memory when they cannot be written (a full disk, or another rexplayer window holding the file) and written in full as soon as they can; a library record missing its path or title is passed over instead of stopping rexplayer from opening; a song whose drive is unplugged while its details are written keeps the edit for later instead of losing it; and a transcript that cannot be saved says why.
- **A settings file edited by hand with a setting twice opens.** rexplayer could not start at all; the last value written now wins.
- **Pressing "next audio track" twice quickly keeps going.** The second press used to find no audio tracks while the file reopened on the first choice; it now moves on from the track just chosen.

## 0.27.0 - 2026-10-21

### Added

- **rexplay runs on a Mac.** The command line now builds for macOS and plays through the Mac's own audio output; it answers the same commands and JSON as on Windows, checked on a Mac on every change. The full macOS player follows.
- **Files still downloading show as such.** A file arriving in a watched folder (named by a downloader, marked by its control file, short of the size its MP4, Matroska, AVI, WAVE or MP3 header or its downloader declares, a torrent's sparse file whose pieces are not all on the disk yet, or one another program is still writing or growing) wears a down-arrow badge with a progress ring on its card, line and shelf tile, and its detail line reads "Downloading · 1.2 GB of 3.4 GB" as it grows, until the moment it is whole. The library's Filter menu gains a Download group (Fully downloaded, Still downloading) that combines with the others and is remembered per view; Home shows a Downloading shelf while anything is partial, newest first, with See all opening the view filtered to it; Play on a file too incomplete to open says "Still downloading: 20% so far" in the library instead of failing, the queue passes over such a file with the same words, and playing a torrent's file buffers where a piece has not arrived yet (and on a seek there) instead of playing the missing part as silence and garbage; Edit details never writes into a file that is still downloading, and says so.
- **Automatic performance mode while Energy Saver is on.** When Windows' Energy Saver turns on, a chosen Best quality, Balanced or Fastest gives way to Automatic so the device budget adapts, with a short notice over the picture; the chosen mode comes back, and is announced, when Energy Saver turns off. Preferences > Performance shows which mode is in effect and why, and "When Energy Saver is on, use the Automatic performance mode" turns the behaviour off.

### Changed

- **Movie details now come from Wikidata and Wikipedia, with no key needed.** The Movies switch in Preferences alone turns lookups on: facts, cast and trailers come from Wikidata, the synopsis and poster from English Wikipedia, and nothing asks for a key or a sign-up. A movie's right-click offers More like this, ranked from the library's own movies by shared series, directors, cast and genres. TV shows TVmaze does not know fall back to Wikidata and Wikipedia too. Movie details fetched before are fetched again once, and a key stored by an earlier version is dropped. Preferences and Help, About credit Wikidata, TVmaze, MusicBrainz, the Cover Art Archive and Wikipedia.

- **Artwork fills its card.** Posters, covers, video thumbnails, episode stills, the billboard and the timeline preview now fill their frames edge to edge, cropped rather than shrunk inside grey bars; poster cards keep the top of the picture, where faces usually are. A card with no picture yet keeps its clean placeholder.
- **Thumbnails without recorded bars.** A letterboxed film or a 4:3 programme in a wide frame gets a thumbnail cut from the picture inside its black or grey bars, so a card shows picture, not bars; video thumbnails are made once more to pick this up.
- **Movies and TV shows open on Browse.** The billboard and rows are the layout for both views unless you pick another: a Cards layout saved only because an order or card size changed, or kept from before Browse existed, opens as Browse; a layout you pick stays.

### Improved

- **One home for rexplayer's files on every platform.** Where settings, logs, the library, bookmarks and caches live is decided once in the shared player code: under Local AppData on Windows and under Library/Application Support on a Mac.

- **The menu bar is one list for every shell.** The menus, their groups and which items carry a tick now live in the shared player code, so the coming macOS build shows exactly the menus Windows does.

- **Shortcuts speak every platform's language.** A shortcut typed the Mac way (Cmd+O, Option+Left) is understood, and the shared player code can show any shortcut in Windows words or Mac words, so one settings file means the same keys on both.

### Fixed

- **No more silent exits from the window's own work.** A failure in work rexplayer hands to its window from elsewhere (a later launch's files, the library, downloads), in one of its clocks, in the library's background scan or in episode analysis used to close rexplayer without a trace; it is now written to the log with its details and the player carries on. A task left to finish on its own that fails is written down as it fails instead of being lost, and a script's request that fails is answered with why instead of being left waiting.

- **A right-click on a library line acts on that line.** With other lines chosen, right-clicking a line that is not among them in the list or the collage now chooses it alone, so Play next, Add to queue and Add to a playlist use the line under the pointer.
- **Down in the library search moves into the results.** It chooses the first item found, so Enter plays it.
- **No more crash while subtitles are being transcribed.** With a transcript being made for what plays, a seek, a pause or the next item could end rexplayer at once: the stretch to hear next sometimes held no sound at all (the file's sound lands later than asked, or there is none there), and the speech model was asked to hear nothing, which the model cannot survive. Such a stretch is now passed over as heard with no words, and the model is never asked to hear an empty piece.

## 0.26.0 - 2026-10-20

### Added

- **Richer facts behind every enriched title.** Shows and movies now cache everything the free catalogues offer - taglines, runtimes, status, network or studio, content rating, language, cast with characters, directors, creators and writers, per-season and per-episode details with air dates and stills, trailer links, suggestions and collections - all offline once fetched; movie cards say more and a movie's right-click offers Watch trailer (opened in your browser).
- **Movies enriched from TMDB with your own free key.** With movie lookups on and a key pasted in Preferences, movies get posters, genres and ratings in the library, fetched once and kept on this computer; artwork beside the files always wins, and a movie's right-click offers Refresh metadata. This product uses the TMDB API but is not endorsed or certified by TMDB.
- **Album covers from MusicBrainz and the Cover Art Archive.** With music lookups on, albums whose files carry no artwork of their own get their real cover, fetched once at no more than one request a second and kept on this computer; embedded and folder art wins unless you choose Replace existing covers too, an album's right-click offers Refresh metadata, Replace cover... (pick from the catalogue or any image, shown by rexplayer without ever writing to your files) and Restore original cover.
- **TV shows enriched from TVmaze.** With TV lookups on, shows get posters, descriptions, genres, ratings and episode titles in the library, fetched once in the background and kept on this computer; a right-click offers Refresh metadata and Wrong match..., and a corrected match outlives every rescan.
- **A Metadata section in Preferences.** rexplayer can now be allowed, kind by kind and strictly off by default, to fetch TV, movie and music details from free online catalogues; it keeps everything fetched on this computer beside the library, never looks inside excluded folders, and the downloads can be measured and cleared in one click.
- **Artist pages.** Opening an artist in the library shows their most played songs and their albums, newest first, instead of one long song list. An album opens from the page into its own track list with the artist, year, song count and total length over it, Back returns to the artist, and Play and Shuffle play the artist's songs without repeating a top song that is also on an album.
- Write a song's title, artist, album, album artist, track, disc, year, genre and cover into MP3 (ID3v2.3/2.4 and ID3v1) and FLAC files safely: a verified copy replaces the file only when its audio is byte-identical.
- Edit details: right-click songs or an album to change title, artist, album, album artist, track, disc, year and genre; several songs at once show "(several values)" for fields that differ. Kept in rexplayer for now, over the files' own tags.
- **A Metadata section in Preferences.** rexplayer can now be allowed, kind by kind and strictly off by default, to fetch TV, movie and music details from free online catalogues; it keeps everything fetched on this computer beside the library, never looks inside excluded folders, and the downloads can be measured and cleared in one click.
- **A streaming-style browse for Movies and TV shows.** Both views now open on a featured billboard - the movie or show you left part-way, else the most watched, else the newest - with Play and one more action, over rows that scroll sideways: Continue watching with progress bars, Recently added, Most watched, the movies' genres, and everything A-Z. The Browse layout sits beside List, Cards, Big cards and Collage, and each view remembers the layout you choose.
- Edit details: right-click songs or an album to change title, artist, album, album artist, track, disc, year and genre; several songs at once show "(several values)" for fields that differ. MP3 and FLAC files take the new details and cover (written once a playing file is free); other files keep them in rexplayer. Preferences > Library chooses "write them into the files" or "keep them in rexplayer only".
- **About rexplayer.** Help, About rexplayer names the version and when it last updated, shows where the settings, library and log live, and leads to What's new, the website, the log folder and a check for updates. Help, What's new in this version opens the release notes at any time, not only from Preferences after an update.

### Improved

- **A macOS build is on its way, and the capability matrix now says where each platform stands.** Every row carries a Windows and a macOS status, and the new parity ledger (`docs/parity.md`) lists each difference between them with its reason, so the two builds cannot drift apart unnoticed.
- **Clearer Preferences.** What the sleep timer does at its time is now chosen under Playback, beside the other playback choices, instead of under History and resume. Loudest volume and Text of older subtitle files say what they do.
- **Visualisations listed by family.** Preferences groups the visualisations under headings, with the ones that use the camera together; saved choices are kept.
- **The shortcut editor names its jumps.** The wheel and side-button choices say how far they jump, and opened from Preferences the editor uses the jump lengths just set there, saved or not.
- **Zoom out from the right-click menu.** The picture's right-click menu offers Zoom out beside Zoom in and Reset zoom.
- **Quick play and queue on song lines.** Pointing at or arrowing onto a song, album or genre line in the library shows Play and Add to queue right on the line, so a single click plays it or queues it without a right-click menu; keyboard focus shows exactly what hovering shows.
- **Artwork fades in.** Covers in the library's lists, cards and banner fade in over their placeholder once decoded instead of popping, and a focused or pointed-at line's artwork loads first.
- **Playback allocates less each frame.** Finding the subtitles on screen, the caption choreography and the visualiser meters and oscilloscope reuse their memory instead of allocating every frame.
- **A paused visualisation rests.** Once the music pauses or ends, the visualisation lets the last sound die away for a second and a half and then stops redrawing a still picture, instead of drawing it up to sixty times a second; it moves again the moment the music plays. Camera visualisations keep following you while paused.
- **A quieter idle window.** With nothing open, no sleep timer and nothing to transcribe, the window's own clocks no longer wake it 132 times a minute: the resume point is saved while something is open, the sleep timer ticks while it is set, and transcription looks at the computer only while it has work.
- **Paused kinetic captions stand still.** While a video is paused the words on screen are left as they are, instead of being worked out, laid out and announced to screen readers again on every display refresh; they move again when playback, a seek, the window's size or the captions' look changes them.
- **Shorter menus.** Finer variants now sit one level in, so each menu reads at a glance: Playback has Jump (every jump length, including the very short and long ones the menu never offered), Speed (with A little faster and A little slower) and Sleep timer; Audio has Sound timing; Subtitles has Subtitle timing, Subtitle size and Kinetic caption style; Video has Window size; View has Library backup. Clear the playlist and Clear the history moved to the end of View, away from Preferences.
- **Library search from the keyboard.** In the library search box, Escape clears the search and Down moves to the first result, so Enter plays it.
- **Letter headings.** Titles that start with an accented letter now go under the plain letter's heading ("Édith" under E) instead of under a separate "É" heading.
- **A minimised window draws nothing.** While the window is minimised, video pictures are no longer uploaded, drawn and presented to nobody (about 1,760 a minute for 30-frame video), and the visualisation and kinetic captions stop drawing; the video keeps decoding and the sound and timeline play on in step. The latest picture is drawn the moment the window comes back.
- **A page for each TV show.** Opening a TV show now goes straight to its episodes: the show's artwork behind its name, a season picker, and Play that names the episode it resumes or starts (such as "Resume S02 E04"). It opens on the season holding the episode to go on with. Each episode is a numbered line with its picture, its title (taken from the file's tags or the words after "S01E03" in its name), where it was left or that it was watched, a progress bar, and its length. Back returns to the list of shows.
- **Cards lift when pointed at or focused.** Library cards rise slightly and show their play mark on hover and on keyboard focus alike, animated by the compositor.

### Fixed

- **No transcript rail over a picture.** While a picture is on screen the caption coverage rail under the timeline, and the wait for captions, stay away; they come back the moment a song or video plays.
- **Reopening the library returns to where you were.** With Preferences set to return to where you left the library, it now opens on the same view or page (an album, an artist, a show, a season), scrolled as it was, with the same line chosen; that is now the default, since what plays was most often opened from there. "Show the playing item" still opens the view holding it.
- **The library remembers your place everywhere.** Every view keeps its scroll place and chosen line, in the session and across restarts; Back returns to the parent with the line you opened chosen and on screen; a view built again after a scan, a new order or a filter stays on the line in view instead of jumping to the top; clearing a search returns to the place before it, and each view keeps its last search for the session.
- **A right-click on a library line acts on that line.** With other lines chosen, right-clicking a line that is not among them in the list or the collage now chooses it alone, so Play next, Add to queue and Add to a playlist use the line under the pointer.
- **Down in the library search moves into the results.** It chooses the first item found, so Enter plays it.
- **A damaged Vorbis track no longer hangs opening.** A codebook whose length runs never moved on, or that claimed more entries or values than its header holds, kept the decoder setting up for many seconds; it is now refused at once and the file plays its other tracks or is reported as damaged. The nightly fuzzer's input joins the corpus.
- **No more crash on closing.** Closing the window within a moment of an on-screen notice (a title, a seek, a saved setting) could end rexplayer with an error instead of closing cleanly; every clock now stops as the window closes, work queued for the window (such as the visualisation letting its memory go) no longer runs once it has closed, and a part that fails to close is written to the log instead.
- **Enhanced Subject Lock download is back in Preferences.** The optional models can again be downloaded, stopped and removed from Preferences, now under Video and pictures.
- **What's new from Preferences opens.** The What's new link beside the last update in Preferences could close Preferences without showing the notes; it now shows them every time.
- **The right title after a late repeat change.** Choosing Repeat one (or shuffle) while a song was already lined up to follow without a gap no longer leaves the window naming the old song, with the new one's length, once the new one begins.
- **Preferences tells the truth about transcription quality.** The Balanced and Fastest modes said transcription would use "at most the Base/Tiny speech model", but no such cap was ever applied: the Fast, Default or Best quality you choose on the caption rail always decides, because transcripts are saved per quality. Preferences now says the quality stays as you choose it; on Fastest it still says the library is transcribed only while nothing plays.
- **Replaying the last item marks it in the queue.** Dragging the seek bar after the queue ended plays the last item again, and the queue panel now shows it as current.
- **Moving through unseekable media says why nothing happened.** Go to time and the jump keys show a short notice on media that cannot be moved through.
- **Full-screen controls stay while a button in them has focus.** Tabbing to a button in the controls bar keeps the bar up, as focus on the bar itself already did.
- **Compilation tracks show under their own artists.** In the library's Artists view a song on a "Various Artists" album is listed under its own artist too, once.
- **Caption waits across quality changes.** Changing the transcription quality from the caption rail or Preferences while playback waits for captions keeps the wait and its overlay until the new quality's saved coverage is known, instead of briefly resuming playback or leaving it paused with no overlay.
- **Switching transcription quality.** Choosing a quality whose speech model is not downloaded yet now fetches it (never on a metered connection unless you ask) instead of silently making no subtitles, and two requests for one model share a single download.
- **Subtitle track choices after a quality switch.** The newly chosen quality is listed once, as its live transcript, rather than also as a frozen copy loaded while another quality was active.
- **Caption waits at launch.** A file opened with rexplayer waits for its captions from the start when "pause media while transcribing" is on, rather than playing uncaptioned until the launch settles.
- **Library right-click menus.** Right-clicking a song, video, picture, album or card that is not selected now selects it and opens its menu, instead of offering "Choose something first" or acting on an earlier selection.
- **Jump names follow your jump lengths.** The menus, the shortcut editor and the shortcut sheet name each jump by the length set in Preferences (such as Jump forward 30 seconds) instead of always saying a minute or five minutes, and each length in Preferences shows the keys that make that jump.
- **TV episodes filed under Movies.** A show's season folder ("Season 6", "S06", "Series 6", "Staffel 6", "Saison 6", "Specials") now makes its videos that show's episodes wherever it lives, even inside a folder called Movies and with no episode number in the file names; they go in natural order. Episode marks are also read in more forms ("s6e1", "6x01-02", "S06E01E02", "Episode 1", "Ep 01", "601" in season 6's folder), while movies such as "Title (2007)" and sequels stay in Movies. Libraries already scanned are sorted again the moment they load.
- **Files that fail to play.** Files that failed before you open, pick or skip to something else no longer count against it. Before, after a few failures, opening a new list that started with a broken file could stop without trying the playable files after it.
- **Play next after removing the playing item.** If you remove the item that is playing from the queue, items you then choose to Play next (or a next episode) go where it was and play next, instead of going to the top of the queue and being passed over. Items added above that spot no longer change what plays next.
- **Empty library views.** When quick filters hide everything in a view, it now names those filters and offers Clear filters, instead of saying the library has nothing of that kind. "Looking through your folders..." now updates when a scan starts or finishes, even if the scan found nothing new.
- **Removing the next item near the end of one.** Removing the item that was already lined up to follow, in the last seconds of what plays, now skips it: what the playlist holds next plays, or playback ends, instead of the removed item playing anyway.
- **Section times stay with their episode.** If the next episode opens while the intro or credits times are being edited, or while chapter suggestions are on screen, Save no longer writes them onto the new episode; rexplayer says nothing was saved instead.
- **Empty or out-of-range section times.** Clearing a time box, or typing a value outside the video, now shows the usual message instead of silently failing to save.
- **Library names for screen readers.** Library lists, cards and home shelves now tell a screen reader which item is playing ("Sungba, now playing"), as the screen already showed. Nothing on screen changes.
- **See all on Your most played.** It now opens Songs ordered by plays, most played first, so the view starts with what the shelf showed, instead of in the usual Songs order. Your own Songs order comes back when you leave the view.
- **Changing view with a search typed.** Choosing another view while the search box holds text now builds the new view once, not twice.

## 0.25.9 - 2026-10-18

### Added

- **Independent transcript corrections.** Edit each transcription quality's words without changing the recognised original. Corrections autosave with Undo, Redo and Reset; original and edited SRT files stay separate.
- **Subtitle track choices.** Select embedded, sidecar, or Fast, Default and Best original/edited transcript tracks from the Subtitles menu.

### Improved

- **Transcription quality beside playback.** Choose Fast, Default or Best on the caption rail and restore the selected model's coverage without borrowing progress or wait state from another model.

## 0.25.8 - 2026-10-18

### Fixed

- **AVI files with trailing metadata.** Accept non-RIFF metadata after a valid AVI movie segment without weakening initial RIFF validation.
- **Queue playback error notices.** Clear a failed file's on-screen error when playback advances to the next item, even when opening titles are turned off.

## 0.25.7 - 2026-10-18

### Fixed

- **Transcript coverage and quality selection.** Restore completed transcript coverage and wait for the selected transcription quality without mistaking an older generated track for usable subtitles.
- **Timeline previews.** Clear stale preview frames and release cached images when changing media or disabling previews.
- **Preferences and shortcuts.** Keep pending Preferences changes while opening and closing the shortcut editor, with independent Save and Cancel behavior.

## 0.25.6 - 2026-10-17

### Improved

- **Preferences are easier to navigate.** Jump directly to a section using a fixed selector,
  with accessible headings, clearer descriptions, and expandable artwork and library
  transcription settings. Edits stay in the dialog as you move between sections;
  Save applies them together and Cancel leaves saved settings unchanged.
- **Transcription quality is clearer.** Choose Fast, Default or Best with model sizes and
  speed-versus-accuracy guidance in Preferences and kinetic caption settings, preserving
  existing saved model choices. Model availability and Download now follow the selected
  model, while a transcription in progress keeps using its original model.

## 0.25.5 - 2026-10-17

### Improved

- **Recently Played stays in playback order.** Show the most recently played item first in
  a simple mixed-media list without unrelated music-only sorting controls.
- **Smaller library pictures on Fastest.** Apply the selected performance tier's picture
  width budget to local artwork, cached pictures, and Windows thumbnails. Discard old
  decoded resolutions when switching tiers so the budget stays effective.

## 0.25.4 - 2026-10-15

### Improved

- **Short-audio resume behavior.** Apply the configurable resume threshold based on the
  opened media's actual tracks and duration, so short audio starts from the beginning.
- **Whole-player CPU and memory measurements.** Measure processor use during timed
  scenarios and memory changes during steady playback, UI and library work.

## 0.25.3 - 2026-10-15

### Added

- **Play VP8 video through Windows.** Decode compatible VP8 tracks using Media Foundation.
- **Show what is playing in the library.** Highlight the current item and reveal its song,
  picture, movie, or TV season when opening the library.
- **Show local download waits.** Indicate when playback needs more bytes from a growing file,
  then clear the message automatically when playback resumes.

### Improved

- **Short audio starts from the beginning.** Skip the resume prompt for short audio files,
  with a configurable duration threshold.
- **Whole-player performance measurements.** Report processor use for timed scenarios and
  start-to-end memory changes during playback, UI and library scenarios alongside peak metrics.

### Fixed

- **Gapless playlist seek bar.** Reset the displayed position as the next track begins.
- **Reliable Windows shell shutdown.** Keep the background shell worker's wake handle
  alive until the worker exits, preventing a shutdown race.

## 0.25.2 - 2026-10-15

### Added

- **Sun/NeXT audio support.** Open AU audio files.
- **Playback while files download.** Start playback from a local file that is still growing.

### Improved

- **Clearer resume choice.** Make the resume decision explicit.
- **More reliable library grouping.** Keep year-qualified releases distinct, including remakes.
- **Easier playlist management.** Improve long-title display and duplicate-entry handling.
- **More responsive transcription queues.** Make queue behavior clearer and more intentional.

## 0.25.1 - 2026-10-15

### Added

- **Peak memory measurements.** Timed launch, open, play and seek measurements report peak
  working-set and private-memory usage alongside current memory and managed-heap metrics.

### Fixed

- **Performance preferences stay selected.** The chosen performance tier is saved and restored
  instead of reverting to Automatic. Changing tiers also keeps the active transcription item's
  model stable, so subtitle buffering and its pause behavior continue uninterrupted.

## 0.25.0 - 2026-10-15

### Added

- **Play AVI videos through Windows decoders.** Open AVI movie files with XviD/MPEG-4,
  Motion JPEG or H.264 video and their audio tracks. rexplayer reads RIFF and AVIX segments,
  uses older file indexes to seek to keyframes, and scans files without an index.

### Improved

- **Uninterrupted caption playback mode.** With *Wait for subtitles before playing* enabled, rexplayer now
  waits for the predicted amount of caption coverage needed to watch the remainder without
  rebuffering, based on conservative transcription speed, playback speed and remaining duration.
  It no longer resumes after an arbitrary 90-second maximum. An empty new transcript holds
  before playback rather than first running out of captions. Progress shows the target;
  playback remains manually resumable, and errors release the wait.
- **Caption preparation overlay.** When playback waits for captions, a clear message over the
  picture shows how much subtitle coverage is ready and how much is needed. **Play now** lets
  you start immediately rather than wait for the target.
- **A clearer choice for subtitle waiting.** Preferences now gives the existing opt-in
  setting its own plain-language explanation, including the possibility of a long wait.
  The same setting controls the new overlay rather than adding a second toggle.
- **The Z shortcut follows what is showing.** While a video or picture is on screen,
  Z moves to the next picture effect and wraps back to Off; for audio-only playback,
  Z continues to move between music visualisations.
- **Audio follows Windows' multimedia output.** Music and videos use the default
  playback device for multimedia, even when Windows routes console sounds elsewhere.

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
