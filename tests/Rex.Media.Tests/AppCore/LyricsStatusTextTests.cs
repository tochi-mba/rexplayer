using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>Only a song says how its lyrics follow it, in plain words, and nothing stays behind.</summary>
public sealed class LyricsStatusTextTests
{
    private static readonly MediaStageView Song = MediaStagePolicy.Resolve(new MediaStageContext(true,
        StreamsKnown: true, HasAudioTrack: true, Kind: LibraryKind.Music, Shelf: AudioShelf.Music));

    private static readonly MediaStageView DirectAudio = MediaStagePolicy.Resolve(new MediaStageContext(true,
        StreamsKnown: true, HasAudioTrack: true));

    [Theory]
    [InlineData(LyricsDisplayMode.Plain, LyricsStatusText.Untimed)]
    [InlineData(LyricsDisplayMode.LineTimed, LyricsStatusText.LineTimed)]
    [InlineData(LyricsDisplayMode.WordTimed, LyricsStatusText.WordTimed)]
    [InlineData(LyricsDisplayMode.MixedTimed, LyricsStatusText.PartlyWordTimed)]
    [InlineData(LyricsDisplayMode.None, LyricsStatusText.None)]
    [InlineData(null, LyricsStatusText.None)]
    public void ASongSaysHowItsLyricsFollowIt(LyricsDisplayMode? mode, string expected)
    {
        Assert.Equal(MediaStageProfile.Song, Song.Profile);
        Assert.Equal(expected, LyricsStatusText.For(Song, true, mode));
        Assert.Equal(expected, LyricsStatusText.For(DirectAudio, true, mode));
    }

    [Theory]
    [InlineData(AudioShelf.Podcasts)]
    [InlineData(AudioShelf.Recordings)]
    [InlineData(AudioShelf.Audiobooks)]
    [InlineData(AudioShelf.Sounds)]
    public void SpokenWordAndSoundsHaveNoLyricsToMiss(AudioShelf shelf)
    {
        var stage = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true, Kind: LibraryKind.Music, Shelf: shelf));

        Assert.Null(LyricsStatusText.For(stage, true, null));
    }

    [Fact]
    public void NothingIsSaidWhileASongOpensOnceItFailedOrWithLyricsSwitchedOff()
    {
        var opening = MediaStagePolicy.Resolve(new MediaStageContext(true, Kind: LibraryKind.Music, Shelf: AudioShelf.Music));
        var failed = MediaStagePolicy.Resolve(new MediaStageContext(true, StreamsKnown: true, HasAudioTrack: true, HasFailure: true));
        var video = MediaStagePolicy.Resolve(new MediaStageContext(true, StreamsKnown: true, HasVideoTrack: true, HasAudioTrack: true));

        Assert.Equal(MediaStageProfile.Song, opening.Profile);
        Assert.Null(LyricsStatusText.For(opening, true, null));
        Assert.Null(LyricsStatusText.For(failed, true, null));
        Assert.Null(LyricsStatusText.For(video, true, null));
        Assert.Null(LyricsStatusText.For(Song, false, LyricsDisplayMode.LineTimed));
    }

    [Fact]
    public void WhatTheOnlineSearchOrTheLyricsFileSaidComesBeforeNoLyrics()
    {
        Assert.Equal("Looking for lyrics online...", LyricsStatusText.For(Song, true, null, "Looking for lyrics online...", "The lyrics file could not be read."));
        Assert.Equal("The lyrics file could not be read.", LyricsStatusText.For(Song, true, null, problem: "The lyrics file could not be read."));
        Assert.Equal(LyricsStatusText.FromLrclib + " " + LyricsStatusText.LineTimed,
            LyricsStatusText.For(Song, true, LyricsDisplayMode.LineTimed, LyricsStatusText.FromLrclib));
        Assert.Equal(LyricsStatusText.WordTimed, LyricsStatusText.For(Song, true, LyricsDisplayMode.WordTimed, "Looking for lyrics online..."));
    }

    [Fact]
    public void TheMinimalInterfaceSaysNothingAboutMissingLyricsButStillAnswersASearch()
    {
        Assert.Null(LyricsStatusText.For(Song, true, null, minimal: true));
        Assert.Equal(LyricsStatusText.LineTimed, LyricsStatusText.For(Song, true, LyricsDisplayMode.LineTimed, minimal: true));
        Assert.Equal("No matching lyrics were found online.", LyricsStatusText.For(Song, true, null, "No matching lyrics were found online.", minimal: true));
    }

    [Fact]
    public void TheWordsArePlainSentences()
    {
        foreach (var text in new[] { LyricsStatusText.None, LyricsStatusText.Untimed, LyricsStatusText.LineTimed,
            LyricsStatusText.WordTimed, LyricsStatusText.PartlyWordTimed, LyricsStatusText.FromLrclib })
        {
            Assert.True(char.IsUpper(text[0]), text);
            Assert.EndsWith(".", text, StringComparison.Ordinal);
            Assert.DoesNotContain(";", text, StringComparison.Ordinal);
            Assert.DoesNotContain("supplied", text, StringComparison.Ordinal);
        }
    }
}
