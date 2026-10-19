using Rex.Media.Containers.Flac;
using Rex.Media.Containers.Mpeg;
using Rex.Media.Containers.Tags;
using Rex.Media.IO;
using Rex.Media.Primitives;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.Containers;

public sealed class TagFileWriterTests : IDisposable
{
    private static readonly byte[] Art = [0x89, (byte)'P', (byte)'N', (byte)'G', 7, 8, 9];

    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rexplayer-tags-" + Guid.NewGuid().ToString("N"))).FullName;

    private static byte[] Mp3Audio => File.ReadAllBytes(RepoPaths.Combine("tests/fixtures/mp3/stereo-44k-128k-cbr.mp3"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AnMp3IsRewrittenWithItsAudioUntouchedAndItsCreationTimeKept()
    {
        var audio = Mp3Audio[(int)Id3v2.LeadingTagsLength(new MemoryByteSource(Mp3Audio), Ct)..];
        var path = Save("Lonely At The Top.mp3", [.. Id3Builder.V24().Text("TIT2", "Old").Text("TCOM", "Ahmed Ololade").Build(), .. audio]);
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetCreationTimeUtc(path, created);

        var result = TagFileWriter.Write(path, new TagEdit { Title = "Lonely At The Top", Artist = "Asake", Cover = new PictureBlock(3, "image/png", Art) }, Ct);

        Assert.Equal(TagWriteStatus.Written, result.Status);
        var info = Info(path);
        Assert.Equal("Lonely At The Top", info.Metadata[MetadataKeys.Title]);
        Assert.Equal("Ahmed Ololade", info.Metadata[MetadataKeys.Composer]);
        Assert.Equal(Art, info.CoverArt);
        var written = File.ReadAllBytes(path);
        Assert.Equal(audio, written[(int)Id3v2.LeadingTagsLength(new MemoryByteSource(written), Ct)..]);
        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void AnMp3WithAnId3v1TagHasBothUpdatedSoAClearedFieldStaysCleared()
    {
        var v1 = new byte[Id3v1.Size];
        "TAG"u8.CopyTo(v1);
        "Old title"u8.CopyTo(v1.AsSpan(3));
        var path = Save("Terminator.mp3", [.. Mp3Audio, .. v1]);

        Assert.Equal(TagWriteStatus.Written, TagFileWriter.Write(path, new TagEdit { Title = string.Empty, Artist = "Asake" }, Ct).Status);

        var info = Info(path);
        Assert.False(info.Metadata.ContainsKey(MetadataKeys.Title));
        Assert.Equal("Asake", info.Metadata[MetadataKeys.Artist]);
        Assert.Equal(Mp3Audio.Length + Id3v1.Size + Id3v2.HeaderSize + Id3v2Writer.Padding + 10 + 1 + 5, new FileInfo(path).Length);
    }

    [Fact]
    public void AFlacIsRewrittenWithItsFramesUntouched()
    {
        var builder = new FlacBuilder { Comments = ["TITLE=Old"], Padding = 4096 };
        var file = builder.Build([FlacBuilder.Tone(5000, 16, 440, 44_100), FlacBuilder.Tone(5000, 16, 550, 44_100, seed: 2)]);
        var path = Save("Sungba.flac", file);

        var result = TagFileWriter.Write(path, new TagEdit { Title = "Sungba", Genre = "Afrobeats", Cover = new PictureBlock(3, "image/png", Art) }, Ct);

        Assert.Equal(TagWriteStatus.Written, result.Status);
        var info = Info(path);
        Assert.Equal("Sungba", info.Metadata[MetadataKeys.Title]);
        Assert.Equal(Art, info.CoverArt);
        Assert.Equal(file[(int)builder.FirstFrameOffset..], File.ReadAllBytes(path)[(int)builder.FirstFrameOffset..]);
        Assert.Equal(TagWriteStatus.Written, TagFileWriter.Write(path, new TagEdit { RemoveCover = true }, Ct).Status);
        Assert.Null(Info(path).CoverArt);
    }

    [Fact]
    public void OtherFormatsAreLibraryOnly()
    {
        Assert.False(TagFileWriter.CanWrite("Sungba.m4a"));
        Assert.True(TagFileWriter.CanWrite("Sungba.FLAC"));
        var result = TagFileWriter.Write(Path.Combine(_folder, "Sungba.ogg"), new TagEdit { Title = "Sungba" }, Ct);

        Assert.Equal(TagWriteStatus.Unsupported, result.Status);
        Assert.Equal(TagFileWriter.LibraryOnlyMessage, result.Message);
    }

    [Fact]
    public void AnEmptyEditLeavesTheFileAlone()
    {
        var path = Save("Sungba.mp3", Mp3Audio);
        var before = File.GetLastWriteTimeUtc(path);

        Assert.Equal(TagWriteStatus.Written, TagFileWriter.Write(path, new TagEdit(), Ct).Status);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void RefusalsLeaveTheOriginalByteForByteAndNoTemporaryFile()
    {
        var edit = new TagEdit { Title = "Terminator" };
        Refused("damaged.flac", [.. "fLaC"u8, 0x80, 0, 0, 34, .. new byte[34], 1, 2], edit);
        Refused("stacked.mp3", [.. Id3Builder.V24().Text("TIT2", "a").Build(), .. Id3Builder.V24().Text("TIT2", "b").Build(), .. Mp3Audio], edit);
        Refused("noise.mp3", new byte[300], edit);
        Refused("id3-before.flac", [.. Id3Builder.V24().Text("TIT2", "a").Build(), .. new FlacBuilder().Build([FlacBuilder.Tone(3000, 16, 440, 44_100)])], edit);
        Refused("genre-number.mp3", Mp3Audio, new TagEdit { Genre = "17" });
        Refused("huge.mp3", [.. Id3Builder.V24().Frame("PRIV", new byte[Id3v2Writer.MaxTagBytes]).Build(), .. Mp3Audio], edit);

        var readOnly = Save("read-only.mp3", Mp3Audio);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        Assert.Equal(TagWriteStatus.Refused, TagFileWriter.Write(readOnly, edit, Ct).Status);
        File.SetAttributes(readOnly, FileAttributes.Normal);
    }

    [Fact]
    public void AFileHeldOpenWithoutSharingIsBusy()
    {
        var path = Save("Sungba.mp3", Mp3Audio);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.Equal(TagWriteStatus.Busy, TagFileWriter.Write(path, new TagEdit { Title = "Sungba" }, Ct).Status);
        }

        Assert.Equal(Mp3Audio, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_folder));
    }

    private void Refused(string name, byte[] contents, TagEdit edit)
    {
        var path = Save(name, contents);

        var result = TagFileWriter.Write(path, edit, Ct);

        Assert.True(result.Status == TagWriteStatus.Refused, name + ": " + result.Message);
        Assert.Equal(contents, File.ReadAllBytes(path));
        Assert.DoesNotContain(Directory.GetFiles(_folder), file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    private string Save(string name, byte[] contents)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    private static MediaInfo Info(string path)
    {
        using var source = new FileByteSource(path);
        using IDisposable demuxer = path.EndsWith(".flac", StringComparison.Ordinal) ? new FlacDemuxer(source, Ct) : new MpegAudioDemuxer(source, Ct);
        return ((Rex.Media.Containers.IDemuxer)demuxer).Info;
    }
}
