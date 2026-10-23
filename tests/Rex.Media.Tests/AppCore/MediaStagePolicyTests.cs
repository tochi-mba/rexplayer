using Rex.Media.AppCore.Library;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.Tests.AppCore;

/// <summary>Stage decisions never depend on or replace the playback engine.</summary>
public sealed class MediaStagePolicyTests
{
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
