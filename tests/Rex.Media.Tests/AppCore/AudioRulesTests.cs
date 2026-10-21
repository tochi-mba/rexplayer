using Rex.Media.AppCore.Library;
using Rex.Media.Library;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>Music against audiobooks, podcasts, recordings and sounds: every rule, in its order, and every tiebreak (LIB-05).</summary>
public sealed class AudioRulesTests
{
    private static readonly string Root = Path.Combine("D:" + Path.DirectorySeparatorChar, "Media");

    /// <summary>An audio file at <paramref name="relative"/>, read, with no tags unless given.</summary>
    internal static LibraryEntry AudioFile(string relative, TimeSpan? length, string? artist = null, string? album = null, string? title = null,
        string? genre = null, int? track = null, int? year = null, string? mediaType = null, int chapters = 0, bool probed = true, DetailOverride? choice = null)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        return new LibraryEntry
        {
            Path = path,
            Kind = LibraryKind.Music,
            Probed = probed,
            Title = title ?? MediaLibrary.TitleOf(path),
            Artist = artist,
            Album = album,
            Genre = genre,
            Track = track,
            Year = year,
            Duration = length,
            MediaType = mediaType,
            Chapters = [.. Enumerable.Range(0, chapters).Select(index => new LibraryChapter(TimeSpan.FromMinutes(index * 10), $"Chapter {index + 1}"))],
            Override = choice,
        };
    }

    private static readonly TimeSpan ThreeMinutes = TimeSpan.FromMinutes(3);

    private static AudioVerdict? Judge(LibraryEntry entry, MusicLookup lookup = MusicLookup.NotSearched, AudioContext? context = null) =>
        AudioRules.Judge(entry, lookup, context);

    private static AudioVerdict Verdict(AudioShelf shelf, AudioRule rule) => new(shelf, rule);

    [Fact]
    [Capability("LIB-05")]
    public void TheAsakeSongsAreMusicByTheirTagsAndLength()
    {
        foreach (var title in new[] { "Lonely At The Top", "Terminator", "Sungba" })
        {
            Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile($"Music/Asake/{title}.mp3", ThreeMinutes, "Asake", "Lungu Boy", genre: "Afrobeats")));
        }

        // A track number alone is a music tag, and so is a title of its own beside an artist.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Music/01.flac", ThreeMinutes, track: 1)));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Music/x.flac", ThreeMinutes, "Asake", title: "Joha")));
        Assert.True(AudioRules.IsMusic(AudioFile("Music/x.flac", ThreeMinutes, "Asake", title: "Joha"), MusicLookup.NotSearched));
    }

    [Fact]
    [Capability("LIB-05")]
    public void VideosAndPicturesAreNeverJudged()
    {
        Assert.Null(Judge(AudioFile("Videos/Clip.mp4", ThreeMinutes) with { Kind = LibraryKind.Video }));
        Assert.Null(Judge(AudioFile("Pictures/a.jpg", null) with { Kind = LibraryKind.Picture }));
        Assert.False(AudioRules.IsMusic(AudioFile("Videos/Clip.mp4", ThreeMinutes) with { Kind = LibraryKind.Video }, MusicLookup.Matched));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule1TheMoveBeatsEverything()
    {
        var book = AudioFile("Audiobooks/Long.m4b", TimeSpan.FromHours(6), genre: "Audiobook");
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Choice), Judge(book with { Override = new DetailOverride { Audio = AudioShelves.Music } }));
        var song = AudioFile("Music/Asake/Sungba.mp3", ThreeMinutes, "Asake", "Mr. Money with the Vibe");
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.Choice), Judge(song with { Override = new DetailOverride { Audio = AudioShelves.Podcasts } }, MusicLookup.Matched));
        Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Choice), Judge(song with { Override = new DetailOverride { Audio = AudioShelves.Audiobooks } }));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Choice), Judge(song with { Override = new DetailOverride { Audio = AudioShelves.Recordings } }));
        Assert.Equal(Verdict(AudioShelf.Sounds, AudioRule.Choice), Judge(song with { Override = new DetailOverride { Audio = AudioShelves.Sounds } }));

        // A choice the store does not know is no choice; other overrides leave the rules alone.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(song with { Override = new DetailOverride { Audio = "elsewhere" } }));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(song with { Override = new DetailOverride { Title = "Sungba" } }));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule2AnM4bOrAnAudiobookGenreIsAnAudiobook()
    {
        // A six-hour .m4b, whatever its tags say.
        Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Format), Judge(AudioFile("Inbox/Long Book.m4b", TimeSpan.FromHours(6), "An Author", "Long Book", genre: "Podcast")));
        foreach (var genre in new[] { "Audiobook", "Audio book", "Audiobooks", "Spoken word", "Spoken & Audio", "Speech", "Rock;Speech" })
        {
            Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Genre), Judge(AudioFile("Inbox/Chapter.mp3", TimeSpan.FromMinutes(40), "An Author", "A Book", genre: genre), MusicLookup.Matched));
        }

        Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.MediaMark), Judge(AudioFile("Inbox/Book.m4a", TimeSpan.FromHours(2), mediaType: MetadataKeys.Audiobook)));

        // The audiobook genre is weighed before the podcast one.
        Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Genre), Judge(AudioFile("Inbox/Both.mp3", ThreeMinutes, genre: "Podcast;Audiobook")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule3APodcastGenreOrTagsIsAPodcast()
    {
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.Genre), Judge(AudioFile("Inbox/The Daily 2024-01-05.mp3", TimeSpan.FromMinutes(25), "The Daily", "The Daily", genre: "Podcast"), MusicLookup.Matched));
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.MediaMark), Judge(AudioFile("Inbox/Episode.m4a", TimeSpan.FromMinutes(25), mediaType: MetadataKeys.Podcast), MusicLookup.Matched));

        // The podcast mark is weighed before a match and before any folder.
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.MediaMark), Judge(AudioFile("Voice Memos/Episode.m4a", TimeSpan.FromMinutes(25), mediaType: MetadataKeys.Podcast)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule4AMatchMakesTheRestMusicBeforeFoldersAndNames()
    {
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Match), Judge(AudioFile("Voice Memos/Recording 001.m4a", ThreeMinutes), MusicLookup.Matched));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Folder), Judge(AudioFile("Voice Memos/Recording 001.m4a", ThreeMinutes), MusicLookup.Unmatched));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule5FoldersAtAnyDepthAndTheTagsThatBeatOnlyRecordingsAndSounds()
    {
        var tagged = (string relative) => AudioFile(relative, ThreeMinutes, "Asake", "Lungu Boy");
        var untagged = (string relative) => AudioFile(relative, ThreeMinutes);
        foreach (var folder in new[] { "Audiobooks", "Audio Books", "Books", "audiobook" })
        {
            Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Folder), Judge(tagged($"{folder}/Some Book/Part.mp3")));
        }

        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.Folder), Judge(tagged("Podcasts/A Show/Episode.mp3")));
        foreach (var folder in new[] { "Voice Memos", "Recordings", "Voice Notes", "WhatsApp Voice Notes" })
        {
            Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Folder), Judge(untagged($"Phone/{folder}/Hello.mp3")));
        }

        foreach (var folder in new[] { "Ringtones", "Notifications", "Alarms", "Samples", "SFX", "Sound effects" })
        {
            Assert.Equal(Verdict(AudioShelf.Sounds, AudioRule.Folder), Judge(untagged($"Phone/{folder}/Chime.mp3")));
        }

        // A tagged three-minute song kept among recordings or sounds is still a song.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(tagged("Recordings/Lonely At The Top.mp3")));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(tagged("Ringtones/Terminator.mp3")));

        // Too long for a song, the tags do not beat the folder.
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Folder), Judge(AudioFile("Recordings/Live set.mp3", TimeSpan.FromMinutes(45), "Asake", "Live")));

        // The nearest folder speaks first.
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Folder), Judge(untagged("Podcasts/Recordings/Hello.mp3")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule6RecordersNamesAreRecordingsEvenWithTags()
    {
        foreach (var name in new[]
        {
            "Recording 001.m4a", "Recording 004.m4a", "Recording.m4a", "Recording (2).m4a", "New Recording 3.m4a", "Voice 003.m4a",
            "Voice_012.mp3", "REC_20240105_101010.wav", "REC001.wav", "AUD-20240105-WA0001.opus", "PTT-20240105-WA0003.opus",
        })
        {
            Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Name), Judge(AudioFile("Phone/" + name, ThreeMinutes)));
        }

        // The name is weighed before the tags.
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Name), Judge(AudioFile("Phone/Recording 001.m4a", ThreeMinutes, "Phone", "Memos")));

        // Words that only start like a recorder's name are not one.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Music/Voices.mp3", ThreeMinutes, "Asake", "Lungu Boy")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule6ABookPartsNameOnALongTaggedFileIsAnAudiobook()
    {
        var part = (string name, TimeSpan length, int? track) => AudioFile("Inbox/" + name + ".mp3", length, "An Author", "A Book", track: track);
        foreach (var name in new[] { "A Book - Part 03 of 12", "A Book Pt 2", "Chapter 7", "A Book CD1", "A Book (Disc 2)" })
        {
            Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Name), Judge(part(name, TimeSpan.FromMinutes(45), null)));
        }

        // A short file, or one numbered as a track, is music; so is a long one with no book title.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(part("A Book - Part 03 of 12", ThreeMinutes, null)));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(part("A Book - Part 03 of 12", TimeSpan.FromMinutes(45), 3)));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Inbox/Mix Part 2.mp3", TimeSpan.FromMinutes(45), "DJ", title: "Mix Part 2 (live)")));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Inbox/Departure.mp3", TimeSpan.FromMinutes(45), "Asake", "Live")));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule6AnEpisodeCodeCountsOnlyWhenFilesBesideItShareIt()
    {
        var show = new[]
        {
            AudioFile("Downloads/Talk/Talk Ep 12.mp3", TimeSpan.FromMinutes(50)),
            AudioFile("Downloads/Talk/Talk #123.mp3", TimeSpan.FromMinutes(50)),
            AudioFile("Downloads/Talk/Talk E045.mp3", TimeSpan.FromMinutes(50)),
            AudioFile("Downloads/Talk/Talk Episode 3 trailer.mp3", TimeSpan.FromMinutes(2)),
        };
        var context = AudioContext.Of(show);
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.EpisodeCode), Judge(show[0], context: context));
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.EpisodeCode), Judge(show[1], context: context));
        Assert.Equal(Verdict(AudioShelf.Podcasts, AudioRule.EpisodeCode), Judge(show[2], context: context));

        // Under ten minutes the code does not count; the file falls back as a recording.
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(show[3], context: context));

        // One file alone is not enough, and nor is a library seen without its neighbours.
        var alone = AudioFile("Downloads/Other/Talk Ep 12.mp3", TimeSpan.FromMinutes(50));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(alone, context: AudioContext.Of([.. show, alone])));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(show[0]));

        // Videos beside them do not make a folder of episodes.
        var videos = new[] { show[0], show[1] with { Kind = LibraryKind.Video } };
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(show[0], context: AudioContext.Of(videos)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule6ChapterMarksOnALongFileMakeAnAudiobook()
    {
        Assert.Equal(Verdict(AudioShelf.Audiobooks, AudioRule.Chapters), Judge(AudioFile("Inbox/Book.mp3", TimeSpan.FromHours(3), "An Author", "Book", chapters: 12)));

        // Short, or with one mark only, it is what its tags say.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Inbox/Song.mp3", ThreeMinutes, "Asake", "Work of Art", chapters: 3)));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Inbox/Mix.mp3", TimeSpan.FromMinutes(45), "DJ", "Mix", chapters: 1)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule6AnUntaggedFileInARecordersFormatIsARecording()
    {
        foreach (var name in new[] { "Lecture.m4a", "Call.amr", "Clip.3gp", "Note.opus" })
        {
            Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.RecorderFormat), Judge(AudioFile("Phone/" + name, TimeSpan.FromMinutes(30))));
        }

        // Tagged, two hours or longer, or not read yet, the format does not decide.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Music/Sungba.m4a", ThreeMinutes, "Asake", "Mr. Money with the Vibe")));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(AudioFile("Phone/Lecture.m4a", TimeSpan.FromHours(3))));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Fallback), Judge(AudioFile("Phone/Lecture.m4a", null, probed: false)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule7TagsAndASongsLengthOrALongMixsAreMusic()
    {
        var mix = AudioFile("Music/Boiler Room Lagos.mp3", TimeSpan.FromMinutes(45), "DJ Tunez", "Boiler Room");
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(mix));
        Assert.True(AudioRules.IsLongMix(mix));
        Assert.False(AudioRules.IsLongMix(AudioFile("Music/Asake/Sungba.mp3", ThreeMinutes, "Asake", "Mr. Money with the Vibe")));
        Assert.False(AudioRules.IsLongMix(AudioFile("Phone/Lecture.mp3", TimeSpan.FromMinutes(45))));

        // An untagged file named like a song counts as tagged.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Downloads/Asake - Lonely At The Top.mp3", TimeSpan.FromMinutes(4))));
    }

    [Fact]
    [Capability("LIB-05")]
    public void Rule8TheFallbackPutsEveryFileSomewhere()
    {
        // Tagged but short: music. Untagged and short: a sound. Untagged and longer: a recording.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Fallback), Judge(AudioFile("Music/Intro.mp3", TimeSpan.FromSeconds(20), "Asake", "Lungu Boy")));
        Assert.Equal(Verdict(AudioShelf.Sounds, AudioRule.Fallback), Judge(AudioFile("Downloads/notification.mp3", TimeSpan.FromSeconds(2))));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(AudioFile("Downloads/Terminator.mp3", TimeSpan.FromMinutes(4))));
        Assert.Equal(Verdict(AudioShelf.Recordings, AudioRule.Fallback), Judge(AudioFile("Downloads/Unknown length.mp3", null)));

        // A file not read yet stays music until its details are read.
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Fallback), Judge(AudioFile("Downloads/Terminator.mp3", null, probed: false)));
        Assert.Equal(Verdict(AudioShelf.Music, AudioRule.Tags), Judge(AudioFile("Downloads/Terminator.mp3", ThreeMinutes, "Asake", "Work of Art", probed: false)));
    }

    [Fact]
    [Capability("LIB-05")]
    public void MusicViewsLeaveTheOtherAudioOutAndKeepVideosAndPictures()
    {
        var song = AudioFile("Music/Asake/Terminator.mp3", ThreeMinutes, "Asake", "Work of Art");
        var memo = AudioFile("Phone/Recording 001.m4a", ThreeMinutes);
        var video = AudioFile("Videos/Clip.mp4", ThreeMinutes) with { Kind = LibraryKind.Video };
        var podcast = AudioFile("Inbox/Show Ep 1.mp3", TimeSpan.FromMinutes(30));
        var nextPodcast = AudioFile("Inbox/Show Ep 2.mp3", TimeSpan.FromMinutes(30));
        Assert.Equal([song, video], AudioRules.WithoutOtherAudio([song, memo, video, podcast, nextPodcast], AudioRules.Offline));
        Assert.Equal([song, memo, video], AudioRules.WithoutOtherAudio([song, memo, video], _ => MusicLookup.Matched));
    }

    [Fact]
    [Capability("LIB-05")]
    public void OnlyWhatAMatchWouldMakeMusicIsLookedUp()
    {
        Assert.True(AudioRules.WorthLookingUp(AudioFile("Music/Asake/Sungba.mp3", ThreeMinutes, "Asake", "Mr. Money with the Vibe")));
        Assert.True(AudioRules.WorthLookingUp(AudioFile("Downloads/Terminator.mp3", ThreeMinutes)));
        Assert.False(AudioRules.WorthLookingUp(AudioFile("Audiobooks/Book.m4b", TimeSpan.FromHours(6), "Author", "Book")));
        Assert.False(AudioRules.WorthLookingUp(AudioFile("Inbox/Show.mp3", ThreeMinutes, "Host", "Show", genre: "Podcast")));
        Assert.False(AudioRules.WorthLookingUp(AudioFile("Music/Asake/Sungba.mp3", ThreeMinutes, "Asake", "Album", choice: new DetailOverride { Audio = AudioShelves.Recordings })));
    }

    [Fact]
    [Capability("LIB-05")]
    public void RankingsLeaveTheLongMixesOut()
    {
        var song = AudioFile("Music/Asake/Sungba.mp3", ThreeMinutes, "Asake", "Mr. Money with the Vibe") with { Plays = 3 };
        var mix = AudioFile("Music/Boiler Room Lagos.mp3", TimeSpan.FromMinutes(45), "Asake", "Boiler Room") with { Plays = 40 };
        Assert.Equal([song], AudioRules.Ranked([song, mix]));
        Assert.Equal([song], HomeShelves.MostPlayed([song, mix], 10));
        Assert.Equal([song], ArtistPages.TopSongs(new LibraryGroup("Asake", null, [song, mix])));
    }

    [Fact]
    [Capability("LIB-05")]
    public void EachShelfHasItsStoredName()
    {
        Assert.Equal(AudioShelves.Music, AudioRules.StoredName(AudioShelf.Music));
        Assert.Equal(AudioShelves.Audiobooks, AudioRules.StoredName(AudioShelf.Audiobooks));
        Assert.Equal(AudioShelves.Podcasts, AudioRules.StoredName(AudioShelf.Podcasts));
        Assert.Equal(AudioShelves.Recordings, AudioRules.StoredName(AudioShelf.Recordings));
        Assert.Equal(AudioShelves.Sounds, AudioRules.StoredName(AudioShelf.Sounds));
        Assert.Equal("", AudioRules.FolderOf("Loose.mp3"));
    }

    [Fact]
    public void NullsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => AudioRules.Judge(null!, MusicLookup.NotSearched));
        Assert.Throws<ArgumentNullException>(() => AudioRules.WithoutOtherAudio(null!, AudioRules.Offline));
        Assert.Throws<ArgumentNullException>(() => AudioRules.WithoutOtherAudio([], null!));
        Assert.Throws<ArgumentNullException>(() => AudioRules.IsLongMix(null!));
        Assert.Throws<ArgumentNullException>(() => AudioRules.Ranked(null!));
        Assert.Throws<ArgumentNullException>(() => AudioContext.Of(null!));
    }
}
