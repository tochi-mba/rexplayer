using Rex.Media.Primitives;
using Rex.Media.Subtitles;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    /// <summary>The picture for what is playing: its own, or the folder's (META-06); null when there is none.</summary>
    public byte[]? CoverArt { get; private set; }

    /// <summary>The lyrics of what is playing, from an LRC file beside it or its own tag (META-09); null when there are none.</summary>
    public Lyrics? Lyrics { get; private set; }

    /// <summary>A local LRC file exists but cannot be read; absent files are not errors.</summary>
    public string? LyricsProblem { get; private set; }

    /// <summary>The line of the lyrics being sung now; -1 when none is.</summary>
    public int LyricIndex => Lyrics?.IndexAt(Position) ?? -1;

    /// <summary>Finds the picture and the lyrics of an item once its details are known.</summary>
    private void FindPresentation(PlaylistItem item, MediaInfo info)
    {
        CoverArt = info.CoverArt ?? ReadOrNull(FolderArt.Find(item.Location, _listFolder));
        var lrc = item.Location.Contains("://", StringComparison.Ordinal) ? null : Path.ChangeExtension(item.Location, ".lrc");
        LyricsProblem = null;
        byte[]? lyricBytes = null;
        if (lrc is not null)
        {
            try
            {
                lyricBytes = _readFile(lrc);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // A song without a sidecar is ordinary, not a load failure.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LyricsProblem = "The lyrics file could not be read.";
                _log.Debug(LogSource, $"Local lyrics were inaccessible: {ex.Message}");
            }
        }

        Lyrics? parsed = null;
        if (lyricBytes is not null)
        {
            parsed = Lyrics.Parse(SubtitleText.Decode(lyricBytes, Settings.SubtitleCodePage).Text);
        }

        Lyrics = parsed ?? Lyrics.Parse(info.Metadata.GetValueOrDefault(MetadataKeys.Lyrics));
        if (CoverArt is not null || Lyrics is not null)
        {
            _log.Debug(LogSource, $"Found {(CoverArt is null ? "no picture" : "a picture")} and {(Lyrics is null ? "no lyrics" : Lyrics.IsTimed ? "timed lyrics" : "lyrics")} for {item.Location}.");
        }
    }

    private void ForgetPresentation()
    {
        CoverArt = null;
        Lyrics = null;
        LyricsProblem = null;
    }

    private byte[]? ReadOrNull(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            return _readFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
