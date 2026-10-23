using Rex.Media.AppCore.Library;
using Rex.Media.Containers.Tags;
using Rex.Media.Library;

namespace Rex.Media.Tests.Library;

public sealed class PendingDetailWritesTests
{
    private static readonly PictureBlock Cover = new(3, "image/png", [1, 2]);

    private static readonly TagWriteResult Written = new(TagWriteStatus.Written, "Saved into the file.");

    [Fact]
    public void AWriteBecomesTheTagWritersEdit()
    {
        var change = new DetailOverride { Title = "Sungba", Artist = "Asake", Album = "Work of Art", AlbumArtist = "Asake", Track = "3", Disc = "1", Year = "2023", Genre = "Afrobeats" };

        var edit = new DetailWrite("Sungba.mp3", change, Cover, RemoveCover: true).ToEdit();

        Assert.Equal(new TagEdit { Title = "Sungba", Artist = "Asake", Album = "Work of Art", AlbumArtist = "Asake", Track = "3", Disc = "1", Date = "2023", Genre = "Afrobeats", Cover = Cover }, edit);
        Assert.True(new DetailWrite("a.mp3", new DetailOverride(), RemoveCover: true).ToEdit().RemoveCover);
    }

    [Fact]
    public void ACoverComesFromAJpegOrPngFile()
    {
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 0];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0];
        Assert.Equal("image/png", DetailWrite.CoverFrom("cover.PNG", png)!.MimeType);
        Assert.Equal("image/jpeg", DetailWrite.CoverFrom("cover.jpeg", jpeg)!.MimeType);
        Assert.Equal("image/jpeg", DetailWrite.CoverFrom("cover.jpg", jpeg)!.MimeType);
        Assert.Null(DetailWrite.CoverFrom("cover.gif", png));
        Assert.Null(DetailWrite.CoverFrom("cover.png", []));
        Assert.Null(DetailWrite.CoverFrom("cover.png", jpeg));
        Assert.Null(DetailWrite.CoverFrom("cover.jpg", png));
        Assert.Null(DetailWrite.CoverFrom("cover.jpg", [1]));
        Assert.Null(DetailWrite.CoverFrom("cover.png", new byte[DetailWrite.MaxCoverBytes + 1]));
        Assert.Throws<ArgumentNullException>(() => DetailWrite.CoverFrom(null!, png));
        Assert.Throws<ArgumentNullException>(() => DetailWrite.CoverFrom("cover.png", null!));
    }

    [Fact]
    public void LaterEditsJoinAWaitingOneAndKeepTheLatestCoverChoice()
    {
        var first = new DetailWrite("a.mp3", new DetailOverride { Title = "Old", Artist = "Asake" }, Cover);

        var joined = first.Then(new DetailWrite("a.mp3", new DetailOverride { Title = "Terminator" }));
        var removed = first.Then(new DetailWrite("a.mp3", new DetailOverride(), RemoveCover: true));

        Assert.Equal(new DetailOverride { Title = "Terminator", Artist = "Asake" }, joined.Change);
        Assert.Same(Cover, joined.Cover);
        Assert.Null(removed.Cover);
        Assert.True(removed.RemoveCover);
    }

    [Fact]
    public void ThePlayingFileWaitsAndABusyFileIsTriedAgainWithNewerEditsOnTop()
    {
        var pending = new PendingDetailWrites();
        pending.Add(new DetailWrite("playing.mp3", new DetailOverride { Title = "Sungba" }));
        pending.Add(new DetailWrite("busy.mp3", new DetailOverride { Title = "Old", Album = "Work of Art" }));
        pending.Add(new DetailWrite("free.mp3", new DetailOverride { Title = "Lonely At The Top" }));
        var written = new List<(string, TagEdit)>();

        var results = pending.Flush(path => path == "playing.mp3", (path, edit) =>
        {
            written.Add((path, edit));
            if (path == "busy.mp3")
            {
                pending.Add(new DetailWrite("busy.mp3", new DetailOverride { Title = "Terminator" }));
                return new TagWriteResult(TagWriteStatus.Busy, "held");
            }

            return Written;
        });

        Assert.Equal(2, results.Count);
        Assert.Equal(2, pending.Count);
        Assert.DoesNotContain(written, item => item.Item1 == "playing.mp3");
        var retried = pending.Flush(_ => false, (path, edit) =>
        {
            written.Add((path, edit));
            return Written;
        });
        Assert.Equal(2, retried.Count);
        Assert.Equal(0, pending.Count);
        var busy = written.Last(item => item.Item1 == "busy.mp3").Item2;
        Assert.Equal("Terminator", busy.Title);
        Assert.Equal("Work of Art", busy.Album);
    }

    [Fact]
    public void ABusyFileWithNothingNewerKeepsItsEdit()
    {
        var pending = new PendingDetailWrites();
        pending.Add(new DetailWrite("busy.mp3", new DetailOverride { Title = "Sungba" }));

        pending.Flush(_ => false, (_, _) => new TagWriteResult(TagWriteStatus.Busy, "held"));

        Assert.Equal(1, pending.Count);
    }
}
