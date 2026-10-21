# Library classification

How the library decides where each file belongs. Every rule here is a pure function with a test
for it; nothing is stored, so a library saved under older rules follows these the moment it opens.

## Audio: Music, Audiobooks, Podcasts, Recordings, Sounds

Source: `src/Rex.Media.AppCore/Library/AudioRules.cs`, tested by
`tests/Rex.Media.Tests/AppCore/AudioRulesTests.cs`.

Every audio file lands on exactly one shelf. Videos and pictures are never judged, so the sound
inside a video (a video podcast included) stays with its video. The first rule that applies wins.

| # | Rule | Shelf |
|---|---|---|
| 1 | The person moved it: right-click "Move to Music", "Move to Audiobooks", "Move to Podcasts" or "Move to Recordings" | As chosen |
| 2 | An `.m4b`; a genre of Audiobook, Audio book, Spoken word or Speech; the tags' audiobook mark (MP4 media kind 2) | Audiobooks |
| 3 | A Podcast genre; the tags' podcast mark (MP4 `pcst`, media kind 21, `purl` or `egid`; ID3 `PCST` or `WFED`) | Podcasts |
| 4 | The music lookups matched its album in MusicBrainz | Music |
| 5 | A folder at any depth (the nearest speaks first): Audiobooks, Audio books, Books / Podcasts / Voice Memos, Recordings, Voice Notes, WhatsApp Voice Notes / Ringtones, Notifications, Alarms, Samples, SFX, Sound effects | Audiobooks / Podcasts / Recordings / Sounds |
| 6a | A recorder's name: `Recording`, `Recording (2)`, `Recording 001`, `New Recording 3`, `Voice 003`, `REC_...`, `AUD-YYYYMMDD...`, `PTT-YYYYMMDD-WA####` | Recordings |
| 6b | "Part 03 of 12", "Chapter 7", "CD1" or "Disc 2" in the name of a file over 20 minutes with an album and an artist but no track number | Audiobooks |
| 6c | An episode code ("Ep 12", "Episode 3", "#123", "E045") on a file over 10 minutes, when another file in the same folder has one too | Podcasts |
| 6d | Two or more chapter marks on a file over 20 minutes | Audiobooks |
| 6e | No music tags, a recorder's format (`.m4a`, `.amr`, `.3gp`, `.opus`) and under 2 hours | Recordings |
| 7 | Music tags and a length of 30 seconds to 20 minutes; tagged files over 20 minutes stay music as long mixes and sets | Music |
| 8 | The fallback: music tags make it music; else under 10 seconds it is a sound; else a recording | Music / Sounds / Recordings |

**Music tags** are an artist with a title of its own or an album, or a track number. A file named
"Artist - Title" counts as tagged.

**Tiebreaks.**

- Music tags with a song's length beat a Recordings or Sounds folder: a tagged three-minute song kept
  among recordings is still a song. They do not beat an Audiobooks or Podcasts folder, which say what
  the files are more surely than tags written by a ripper.
- A recorder's name beats tags (rule 6 comes before rule 7): `Recording 004.m4a` is a recording
  whatever its tags say, and so is an untagged one of three minutes.
- The audiobook marks (rule 2) beat the podcast marks (rule 3), and both beat a lookup match, because
  the music catalogues list audiobooks and spoken word too.
- A file whose details are not read yet stays music until they are, so a library being read does not
  empty Songs.

**Long mixes and sets** (tagged music over 20 minutes) are music, set apart under "Long mixes and
sets" at the foot of Songs, and kept out of "Your most played" and an artist's top songs.

**Moves** are kept in the library's own store with the person's other edits (`DetailOverride.Audio`),
so rescans, written tags and clearing fetched metadata never undo them.

## The listening views

Source: `src/Rex.Media.AppCore/Library/ListeningViews.cs` and `ListeningSources.cs`, tested by
`tests/Rex.Media.Tests/AppCore/ListeningViewsTests.cs` and
`tests/Rex.Media.Tests/Library/ListeningLibraryTests.cs`.

Audiobooks, Podcasts and Recordings are three views of their own rather than one "Other audio" view
with groups: each is something people go to on purpose, and listing only the ones that have something
in them keeps the sidebar as short as it was for a library of music alone. Sounds are a small group at
the foot of Recordings rather than a fourth view, since a library rarely holds more than a few.

- **Which views are listed.** Each view is listed, after the music views and always in the order
  Audiobooks, Podcasts, Recordings, only while it has something in it. The first look shows what is
  there at once; after that a change shows once it has held for two seconds with no scan running, so a
  scan never makes a view blink in and out. A person's own move shows at once. A view that empties while
  it is open returns to Home and says so.
- **Books** are an album tag, a folder (a "Disc 2", "CD1" or "Part 3" folder belongs to the book above
  it) and an author, so several books in one folder part by album and a book in many files is one book.
  A loose book is named after its title without its part number. Parts are read in disc, track, then
  natural name order. A book's chapters are its files' chapter marks, else one a part. Where a book is
  up to is the part played last, at the point it was left; its speed and "Mark as finished" are kept per
  book.
- **Shows** are an album tag, else an artist, else a folder, compared without case or punctuation, so
  one show in two folders is one show. Episodes are newest first by their year tag (the file's date when
  that is in the same year), else the file's date, else a date in the name.
- **Recordings** are newest first by a date in the name, else the file's date, under Today, Yesterday,
  This week and then each month; WhatsApp voice notes under WhatsApp; those with no date under Undated;
  sounds last.
