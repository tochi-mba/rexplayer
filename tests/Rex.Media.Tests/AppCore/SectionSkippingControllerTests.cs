using System.Reflection;
using Rex.Media.AppCore.Commands;
using Rex.Media.AppCore.Player;
using Rex.Media.Containers.Matroska;
using Rex.Media.Engine;
using Rex.Media.Library;
using Rex.Media.Settings;
using Rex.Media.TestKit;
using Rex.Media.Tests.Containers;

namespace Rex.Media.Tests.AppCore;

/// <summary>
/// The skip button's behaviour through the controller: the next episode at the credits, the
/// keyboard commands, and opted-in automatic skipping with its countdown and cancel.
/// </summary>
public sealed class SectionSkippingControllerTests
{
    /// <summary>Ten seconds of seekable video, a keyframe every tenth of a second.</summary>
    private static byte[] TenSeconds()
    {
        var video = EbmlWriter.Element(MatroskaId.TrackEntry,
            EbmlWriter.UInt(MatroskaId.TrackNumber, 2),
            EbmlWriter.UInt(MatroskaId.TrackType, 1),
            EbmlWriter.Text(MatroskaId.CodecId, "V_MPEG4/ISO/AVC"),
            EbmlWriter.Element(MatroskaId.Video,
                EbmlWriter.UInt(MatroskaId.PixelWidth, 16),
                EbmlWriter.UInt(MatroskaId.PixelHeight, 8)));
        var info = EbmlWriter.Element(MatroskaId.Info,
            EbmlWriter.UInt(MatroskaId.TimestampScale, 1_000_000),
            EbmlWriter.Float(MatroskaId.Duration, 10_000));
        return MatroskaCraftedTests.Mkv(info, MatroskaCraftedTests.Tracks(video),
            MatroskaCraftedTests.Cluster(0,
                [.. Enumerable.Range(0, 100).Select(i => MatroskaCraftedTests.Simple(2, (short)(i * 100), true, (byte)i))]));
    }

    private static readonly MethodInfo MaybeSkip = typeof(PlayerController).GetMethod("MaybeSkipAutomatically",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    private sealed class Season : IDisposable
    {
        public Season(params string[] names)
        {
            Folder = Directory.CreateTempSubdirectory("rexplayer-season-").FullName;
            var bytes = TenSeconds();
            Paths = [.. names.Select(name => Path.Combine(Folder, name))];
            foreach (var path in Paths)
            {
                File.WriteAllBytes(path, bytes);
            }

            Harness = new ControllerHarness(autoPlay: false, pictures: true, store: Store);
            foreach (var path in Paths)
            {
                Harness.Files[path] = bytes;
            }
        }

        public string Folder { get; }

        public string[] Paths { get; }

        public RexStore Store { get; } = RexStore.InMemory();

        public ControllerHarness Harness { get; }

        public PlayerController Player => Harness.Controller;

        public void Open(params string[] paths)
        {
            Player.Open(paths);
            Harness.PumpUntil(player => player.State == SessionState.Ready && player.Info is not null);
        }

        public void Dispose()
        {
            Harness.Dispose();
            Directory.Delete(Folder, recursive: true);
        }
    }

    [Fact]
    public void AtTheLastCreditsTheButtonPlaysTheNextEpisodeFromTheFolder()
    {
        using var season = new Season("Show.S01E01.mkv", "Show.S01E02.mkv", "Show.S01E02.Featurette.mkv", "notes.txt");
        var (first, second) = (season.Paths[0], season.Paths[1]);
        new PlayerMemory(season.Store).SetSections(first, new EpisodeSections(CreditsStart: TimeSpan.FromSeconds(6), CreditsEnd: TimeSpan.FromSeconds(10)));
        season.Open(first);
        season.Harness.PumpUntil(player => player.NextEpisode == second);
        Assert.Equal(new EpisodeIdentity("SHOW", 1, 1), season.Player.CurrentEpisode);
        Assert.Null(season.Player.SkipPrompt);

        season.Player.Seek(TimeSpan.FromSeconds(7));
        var prompt = Assert.IsType<EpisodeSkipPrompt>(season.Player.SkipPrompt);
        Assert.Equal(EpisodeSkipAction.NextEpisode, prompt.Primary);
        Assert.False(prompt.Detected);
        Assert.True(season.Player.SkipEpisodeSection());
        season.Harness.PumpUntil(player => player.Item?.Location == second && player.State == SessionState.Ready);
        Assert.Equal([first, second], season.Player.Playlist.Items.Select(item => item.Location));
        Assert.Null(season.Player.NextEpisode);
        Assert.False(season.Player.PlayNextEpisode());
    }

    [Fact]
    public void TheNextEpisodeAlreadyQueuedIsPlayedInsteadOfAddedAgain()
    {
        using var season = new Season("Show.S01E01.mkv", "Show.S01E02.mkv");
        season.Open(season.Paths);
        season.Harness.PumpUntil(player => player.NextEpisode == season.Paths[1]);
        Assert.True(season.Player.Execute(CommandCatalog.NextEpisode));
        season.Harness.PumpUntil(player => player.Item?.Location == season.Paths[1]);
        Assert.Equal(2, season.Player.Playlist.Items.Count);
    }

    [Fact]
    public void TheShortcutsSayWhenThereIsNothingToDo()
    {
        using var season = new Season("Show.S01E01.mkv");
        season.Open(season.Paths[0]);
        Assert.True(season.Player.Execute(CommandCatalog.SkipSection));
        Assert.Contains("There is no intro or credits to skip here.", season.Harness.Messages);
        Assert.True(season.Player.Execute(CommandCatalog.NextEpisode));
        Assert.Contains("There is no next episode in this folder.", season.Harness.Messages);

        new PlayerMemory(season.Store).SetSections(season.Paths[0], new EpisodeSections(TimeSpan.Zero, TimeSpan.FromSeconds(4)));
        season.Player.Seek(TimeSpan.FromSeconds(1));
        Assert.True(season.Player.Execute(CommandCatalog.SkipSection));
        Assert.Equal(TimeSpan.FromSeconds(4), season.Player.Position);
        Assert.Contains("Intro skipped.", season.Harness.Messages);
        Assert.NotNull(CommandCatalog.Find(CommandCatalog.SkipSection));
        Assert.NotEmpty(new Keymap().ShortcutsFor(CommandCatalog.NextEpisode));
    }

    [Fact]
    public void AlwaysSkipForThisShowCountsDownAndCanBeCancelled()
    {
        using var season = new Season("Show.S01E01.mkv", "Show.S01E02.mkv");
        var first = season.Paths[0];
        new PlayerMemory(season.Store).SetSections(first, new EpisodeSections(TimeSpan.Zero, TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(10)));
        season.Open(first);
        var player = season.Player;
        Assert.Equal(new ShowSkipping(false, false), player.CurrentShowSkipping);
        Assert.False(player.CancelAutomaticSkip());

        // Off: inside the intro nothing counts down.
        player.Seek(TimeSpan.FromSeconds(1));
        MaybeSkip.Invoke(player, null);
        Assert.Null(player.SkipPrompt!.AutomaticInSeconds);

        Assert.True(player.SetShowSkipping(EpisodeSectionKind.Intro, true));
        Assert.Equal(new ShowSkipping(true, false), new PlayerMemory(season.Store).ShowSkips("SHOW"));
        MaybeSkip.Invoke(player, null);
        // The countdown never runs past the section: due at 3 s, a second before the intro ends.
        Assert.Equal(2, player.SkipPrompt!.AutomaticInSeconds);
        Assert.Equal(TimeSpan.FromSeconds(1), player.Position);
        Assert.True(player.CancelAutomaticSkip());
        Assert.Contains("Automatic skip cancelled.", season.Harness.Messages);
        Assert.Null(player.SkipPrompt!.AutomaticInSeconds);
        player.Seek(TimeSpan.FromSeconds(3.5));
        MaybeSkip.Invoke(player, null);
        Assert.Equal(TimeSpan.FromSeconds(3.5), player.Position);

        // Outside every section a pending countdown is forgotten.
        Assert.True(player.SetShowSkipping(EpisodeSectionKind.Credits, true));
        player.Seek(TimeSpan.FromSeconds(6.5));
        MaybeSkip.Invoke(player, null);
        Assert.Equal(3, player.SkipPrompt!.AutomaticInSeconds);
        player.Seek(TimeSpan.FromSeconds(5));
        MaybeSkip.Invoke(player, null);
        player.Seek(TimeSpan.FromSeconds(8.5));
        MaybeSkip.Invoke(player, null);
        Assert.Equal(1, player.SkipPrompt!.AutomaticInSeconds);
        season.Harness.PumpUntil(p => p.NextEpisode is not null);
        player.Seek(TimeSpan.FromSeconds(9.2));
        MaybeSkip.Invoke(player, null);
        season.Harness.PumpUntil(p => p.Item?.Location == season.Paths[1]);

        Assert.True(player.SetShowSkipping(EpisodeSectionKind.Credits, false));
        Assert.Contains("This show's credits will no longer be skipped automatically.", season.Harness.Messages);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SetShowSkipping((EpisodeSectionKind)5, true));
    }

    [Fact]
    public void ThePreferenceSkipsEveryShowAndANonEpisodeHasNoShowToRemember()
    {
        var folder = Directory.CreateTempSubdirectory("rexplayer-film-").FullName;
        try
        {
            var film = Path.Combine(folder, "film.mkv");
            var bytes = TenSeconds();
            File.WriteAllBytes(film, bytes);
            var store = RexStore.InMemory();
            new PlayerMemory(store).SetSections(film, new EpisodeSections(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
            using var harness = new ControllerHarness(autoPlay: false, pictures: true, store: store,
                settings: new PlayerSettings { SkipIntrosAutomatically = true });
            harness.Files[film] = bytes;
            var player = harness.Controller;
            player.Open([film]);
            harness.PumpUntil(p => p.State == SessionState.Ready && p.Info is not null);
            Assert.Null(player.CurrentEpisode);
            Assert.False(player.SetShowSkipping(EpisodeSectionKind.Intro, true));
            player.Seek(TimeSpan.FromSeconds(1.5));
            MaybeSkip.Invoke(player, null);
            player.Seek(TimeSpan.FromSeconds(4.2));
            MaybeSkip.Invoke(player, null);
            Assert.Equal(TimeSpan.FromSeconds(5), player.Position);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
