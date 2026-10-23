using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>Stage decisions never depend on or replace the playback engine.</summary>
public sealed class MediaStagePolicyTests
{
    [Fact]
    public void StageAutomationNamesAndHelpRemainConsistentAcrossReadinessBoundaries()
    {
        var welcome = MediaStagePolicy.Resolve(new MediaStageContext(false));
        Assert.Equal("Playback stage", welcome.AutomationName);
        Assert.Contains("cannot be changed", welcome.AutomationHelpText, StringComparison.Ordinal);

        var loading = MediaStagePolicy.Resolve(new MediaStageContext(true));
        Assert.Equal("OPENING playback stage", loading.AutomationName);
        Assert.Contains("opening", loading.AutomationHelpText, StringComparison.Ordinal);

        var unsupported = MediaStagePolicy.Resolve(new MediaStageContext(true, StreamsKnown: true));
        Assert.Equal("UNSUPPORTED MEDIA playback stage", unsupported.AutomationName);
        Assert.Contains("no playable", unsupported.AutomationHelpText, StringComparison.Ordinal);

        var error = MediaStagePolicy.Resolve(new MediaStageContext(true, HasFailure: true));
        Assert.Equal("PLAYBACK ERROR playback stage", error.AutomationName);
        Assert.Contains("could not be played", error.AutomationHelpText, StringComparison.Ordinal);

        var picture = MediaStagePolicy.Resolve(new MediaStageContext(true, IsPicture: true));
        Assert.Equal("PICTURE playback stage", picture.AutomationName);
        Assert.Contains("Picture view", picture.AutomationHelpText, StringComparison.Ordinal);

        var seekableAudio = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true, CanSeek: true));
        Assert.Equal("AUDIO playback stage", seekableAudio.AutomationName);
        Assert.Contains("seek slider", seekableAudio.AutomationHelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void SpokenAudioInstructionsDisappearWhenCapabilitiesOrProfilesChange()
    {
        var source = new MediaStageContext(true, StreamsKnown: true, HasAudioTrack: true,
            CanSeek: true, Shelf: AudioShelf.Audiobooks, HasChapters: true);
        var spoken = MediaStagePolicy.Resolve(source);
        Assert.Contains("seek slider", spoken.AutomationHelpText, StringComparison.Ordinal);
        Assert.Contains("Chapter jumps", spoken.AutomationHelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("Lyrics", spoken.AutomationHelpText, StringComparison.Ordinal);

        var song = MediaStagePolicy.Resolve(source with
        {
            Shelf = AudioShelf.Music, HasChapters = false, HasLyrics = true, HasCaptions = true,
        });
        Assert.Contains("Lyrics", song.AutomationHelpText, StringComparison.Ordinal);
        Assert.Contains("Captions", song.AutomationHelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("Chapter jumps", song.AutomationHelpText, StringComparison.Ordinal);

        var picture = MediaStagePolicy.Resolve(source with { IsPicture = true });
        Assert.DoesNotContain("Chapter jumps", picture.AutomationHelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("seek slider", picture.AutomationHelpText, StringComparison.Ordinal);

        var failed = MediaStagePolicy.Resolve(source with { HasFailure = true });
        Assert.DoesNotContain("Chapter jumps", failed.AutomationHelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("seek slider", failed.AutomationHelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyOpeningAndUnreadableItemsNeverInventMediaControls()
    {
        var welcome = MediaStagePolicy.Resolve(new MediaStageContext(false));
        Assert.Equal(MediaStageProfile.Welcome, welcome.Profile);
        Assert.Equal("WELCOME", welcome.Label);
        Assert.False(welcome.CanSeek);

        var opening = MediaStagePolicy.Resolve(new MediaStageContext(true));
        Assert.Equal(MediaStageProfile.Loading, opening.Profile);
        Assert.Equal("OPENING", opening.Label);
        Assert.False(opening.ShowAudioPresentation);

        var unsupported = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, CanSeek: true, Kind: LibraryKind.Video));
        Assert.Equal(MediaStageProfile.Unsupported, unsupported.Profile);
        Assert.False(unsupported.CanSeek);
        Assert.False(unsupported.ShowEpisodeContext);
    }

    [Fact]
    public void PictureTakesPriorityAndNeverOffersASeekTimelineOrSongLyrics()
    {
        var picture = MediaStagePolicy.Resolve(new MediaStageContext(true,
            IsPicture: true, StreamsKnown: true, HasAudioTrack: true, CanSeek: true,
            Kind: LibraryKind.Music, HasLyrics: true));
        Assert.Equal(MediaStageProfile.Picture, picture.Profile);
        Assert.True(picture.ShowPictureNavigation);
        Assert.False(picture.ShowAudioPresentation);
        Assert.False(picture.ShowLyrics);
        Assert.False(picture.CanSeek);

        var loadingPicture = MediaStagePolicy.Resolve(new MediaStageContext(true, Kind: LibraryKind.Picture));
        Assert.Equal(MediaStageProfile.Picture, loadingPicture.Profile);
    }

    [Theory]
    [InlineData(false, false, MediaStageProfile.Video, "VIDEO")]
    [InlineData(false, true, MediaStageProfile.Movie, "MOVIE")]
    [InlineData(true, false, MediaStageProfile.Television, "TV EPISODE")]
    public void RealVideoTracksResolveToKnownLibraryLayouts(bool episode, bool movie, MediaStageProfile expected, string label)
    {
        var view = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasVideoTrack: true, HasAudioTrack: true, CanSeek: true,
            Kind: LibraryKind.Video, IsEpisode: episode, IsMovie: movie, HasLyrics: true));
        Assert.Equal(expected, view.Profile);
        Assert.Equal(label, view.Label);
        Assert.Equal(episode, view.ShowEpisodeContext);
        Assert.True(view.CanSeek);
        Assert.False(view.ShowAudioPresentation);
        Assert.False(view.ShowLyrics);
    }

    [Theory]
    [InlineData(AudioShelf.Music, MediaStageProfile.Song, "NOW PLAYING")]
    [InlineData(AudioShelf.Audiobooks, MediaStageProfile.Audiobook, "AUDIOBOOK")]
    [InlineData(AudioShelf.Podcasts, MediaStageProfile.Podcast, "PODCAST")]
    [InlineData(AudioShelf.Recordings, MediaStageProfile.Recording, "RECORDING")]
    [InlineData(AudioShelf.Sounds, MediaStageProfile.Sound, "AUDIO")]
    public void AudioShelvesResolveWithoutInventingLyricsOrChapters(
        AudioShelf shelf, MediaStageProfile expected, string label)
    {
        var view = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true, CanSeek: true,
            Kind: LibraryKind.Music, Shelf: shelf, HasLyrics: true, HasChapters: true));
        Assert.Equal(expected, view.Profile);
        Assert.Equal(label, view.Label);
        Assert.True(view.ShowAudioPresentation);
        Assert.Equal(expected == MediaStageProfile.Song, view.ShowLyrics);
        Assert.Equal(expected is MediaStageProfile.Audiobook or MediaStageProfile.Podcast, view.ShowChapters);
        Assert.True(view.CanSeek);
        Assert.False(view.ShowPictureNavigation);
    }

    [Fact]
    public void DirectAudioWithProvenLyricsRetainsLyricsWithoutAStoredMusicShelf()
    {
        // Opening a song from a path does not require adding it to the media library.
        var direct = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true, HasLyrics: true));
        Assert.Equal(MediaStageProfile.Audio, direct.Profile);
        Assert.True(direct.ShowAudioPresentation);
        Assert.True(direct.ShowLyrics);
        Assert.False(direct.CanSeek);

        var withoutLyrics = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true));
        Assert.False(withoutLyrics.ShowLyrics);

        var notReady = MediaStagePolicy.Resolve(new MediaStageContext(true,
            HasLyrics: true));
        Assert.Equal(MediaStageProfile.Loading, notReady.Profile);
        Assert.False(notReady.ShowLyrics);
    }

    [Fact]
    public void AFailedItemClearsOldAffordancesEvenIfTheOldMediaHadTracks()
    {
        var failed = MediaStagePolicy.Resolve(new MediaStageContext(true,
            HasFailure: true, StreamsKnown: true, HasVideoTrack: true,
            HasAudioTrack: true, HasLyrics: true, HasChapters: true, CanSeek: true,
            Kind: LibraryKind.Music, Shelf: AudioShelf.Audiobooks));
        Assert.Equal(MediaStageProfile.Error, failed.Profile);
        Assert.Equal("PLAYBACK ERROR", failed.Label);
        Assert.False(failed.ShowAudioPresentation);
        Assert.False(failed.ShowLyrics);
        Assert.False(failed.ShowChapters);
        Assert.False(failed.CanSeek);
        Assert.False(failed.ShowPictureNavigation);
    }

    [Fact]
    public void TransitioningBetweenStreamsNeverKeepsUnsupportedActions()
    {
        var source = new MediaStageContext(true, StreamsKnown: true,
            HasAudioTrack: true, HasLyrics: true, CanSeek: true);
        Assert.True(MediaStagePolicy.Resolve(source).ShowLyrics);
        Assert.False(MediaStagePolicy.Resolve(source with { HasVideoTrack = true }).ShowLyrics);
        Assert.False(MediaStagePolicy.Resolve(source with { StreamsKnown = false }).ShowLyrics);
        Assert.False(MediaStagePolicy.Resolve(source with { HasAudioTrack = false }).ShowLyrics);
        Assert.False(MediaStagePolicy.Resolve(source with { IsPicture = true }).ShowLyrics);
        Assert.True(MediaStagePolicy.Resolve(source).ShowLyrics);
    }

    [Theory]
    [InlineData(AudioShelf.Audiobooks, MediaStageProfile.Audiobook)]
    [InlineData(AudioShelf.Podcasts, MediaStageProfile.Podcast)]
    public void ADirectTaggedSpokenAudioStreamRetainsItsRole(AudioShelf shelf, MediaStageProfile profile)
    {
        var view = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true, CanSeek: true,
            Shelf: shelf, HasChapters: true));
        Assert.Equal(profile, view.Profile);
        Assert.True(view.ShowAudioPresentation);
        Assert.True(view.ShowChapters);
        Assert.False(view.ShowLyrics);
        Assert.True(view.CanSeek);
        Assert.Equal(MediaStageProfile.Loading,
            MediaStagePolicy.Resolve(new MediaStageContext(true, Shelf: shelf)).Profile);
    }

    [Theory]
    [InlineData(AudioShelf.Audiobooks)]
    [InlineData(AudioShelf.Podcasts)]
    public void ChapterActionsRequireCurrentStreamReadinessAndSeekSupport(AudioShelf shelf)
    {
        var ready = new MediaStageContext(true, StreamsKnown: true, HasAudioTrack: true,
            CanSeek: true, Kind: LibraryKind.Music, Shelf: shelf, HasChapters: true);
        Assert.True(MediaStagePolicy.Resolve(ready).ShowChapters);
        Assert.False(MediaStagePolicy.Resolve(ready with { CanSeek = false }).ShowChapters);
        Assert.False(MediaStagePolicy.Resolve(ready with { StreamsKnown = false }).ShowChapters);
        Assert.False(MediaStagePolicy.Resolve(ready with { HasAudioTrack = false }).ShowChapters);
        Assert.False(MediaStagePolicy.Resolve(ready with { HasChapters = false }).ShowChapters);
        Assert.False(MediaStagePolicy.Resolve(ready with { HasVideoTrack = true }).ShowChapters);
        Assert.False(MediaStagePolicy.Resolve(ready with { HasFailure = true }).ShowChapters);
    }

    [Fact]
    public void KineticCaptionsRequireAProvenVideoRatherThanAPictureOrAudioOnlyTrack()
    {
        var video = new MediaStageContext(true, StreamsKnown: true, HasVideoTrack: true);
        Assert.True(MediaStagePolicy.Resolve(video).SupportsKineticCaptions);
        Assert.True(MediaStagePolicy.Resolve(video with { Kind = LibraryKind.Video, IsMovie = true }).SupportsKineticCaptions);
        Assert.False(MediaStagePolicy.Resolve(video with { IsPicture = true }).SupportsKineticCaptions);
        Assert.False(MediaStagePolicy.Resolve(video with { StreamsKnown = false }).SupportsKineticCaptions);
        Assert.False(MediaStagePolicy.Resolve(video with { HasVideoTrack = false }).SupportsKineticCaptions);
        Assert.False(MediaStagePolicy.Resolve(video with { HasVideoTrack = false, HasAudioTrack = true }).SupportsKineticCaptions);
        Assert.False(MediaStagePolicy.Resolve(video with { HasFailure = true }).SupportsKineticCaptions);
    }

    [Fact]
    public void CaptionsRequireASelectedTrackAndAProvenPlayableStream()
    {
        var audio = new MediaStageContext(true, StreamsKnown: true,
            HasAudioTrack: true, HasCaptions: true);
        Assert.True(MediaStagePolicy.Resolve(audio).ShowCaptions);
        Assert.False(MediaStagePolicy.Resolve(audio with { HasCaptions = false }).ShowCaptions);
        Assert.False(MediaStagePolicy.Resolve(audio with { StreamsKnown = false }).ShowCaptions);
        Assert.False(MediaStagePolicy.Resolve(audio with { HasAudioTrack = false }).ShowCaptions);
        Assert.False(MediaStagePolicy.Resolve(audio with { IsPicture = true }).ShowCaptions);
        Assert.False(MediaStagePolicy.Resolve(audio with { HasFailure = true }).ShowCaptions);
        Assert.True(MediaStagePolicy.Resolve(audio with { HasAudioTrack = false, HasVideoTrack = true }).ShowCaptions);
        Assert.False(MediaStagePolicy.Resolve(new MediaStageContext(false, HasCaptions: true)).ShowCaptions);
    }

    [Fact]
    public void StreamFactsOverrideWrongLibraryLabelsAndSeekingIsIndependent()
    {
        var audioOnly = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasAudioTrack: true, Kind: LibraryKind.Video,
            IsEpisode: true, IsMovie: true, CanSeek: false));
        Assert.Equal(MediaStageProfile.Audio, audioOnly.Profile);
        Assert.True(audioOnly.ShowAudioPresentation);
        Assert.False(audioOnly.ShowEpisodeContext);
        Assert.False(audioOnly.CanSeek);

        var directVideo = MediaStagePolicy.Resolve(new MediaStageContext(true,
            StreamsKnown: true, HasVideoTrack: true, HasAudioTrack: false, CanSeek: false));
        Assert.Equal(MediaStageProfile.Video, directVideo.Profile);
        Assert.False(directVideo.CanSeek);

        var openingMovie = MediaStagePolicy.Resolve(new MediaStageContext(true,
            Kind: LibraryKind.Video, IsMovie: true, CanSeek: true));
        Assert.Equal(MediaStageProfile.Movie, openingMovie.Profile);
        Assert.False(openingMovie.CanSeek);

        var openingSong = MediaStagePolicy.Resolve(new MediaStageContext(true,
            Kind: LibraryKind.Music, Shelf: AudioShelf.Music, HasLyrics: true));
        Assert.Equal(MediaStageProfile.Song, openingSong.Profile);
        Assert.False(openingSong.ShowAudioPresentation);
        Assert.False(openingSong.ShowLyrics, "Do not display a previous song's lyrics before the new stream opens.");
        Assert.False(openingSong.ShowChapters);

        var openingBook = MediaStagePolicy.Resolve(new MediaStageContext(true,
            Kind: LibraryKind.Music, Shelf: AudioShelf.Audiobooks, HasChapters: true));
        Assert.False(openingBook.ShowChapters, "Chapters should wait until the current stream is known.");
    }
}
