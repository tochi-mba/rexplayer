using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// What the library files each file of one listing as (LIB-05): what
/// <see cref="MediaFiles.LibraryKindOf"/> says, except that artwork is not a picture of its own. A
/// picture named as folder art (an album's cover, folder, front or AlbumArt picture, as
/// <see cref="FolderArt.IsArtName"/> knows them; a show's, season's or movie's poster, folder, show,
/// cover or default picture, as <see cref="ArtworkChoice.IsFolderArtName"/> knows them) belongs to
/// the music or video it sits with when its folder holds music or video, or is the show's folder
/// above a season folder of episodes: the places rexplayer looks for that media's art. The same
/// name among photos alone stays a picture. Only the listing's names are read, so tests need no disk.
/// </summary>
public sealed class LibraryKinds
{
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _artPlaces;

    private LibraryKinds(HashSet<string> artPlaces) => _artPlaces = artPlaces.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The kinds for one listing: <paramref name="paths"/> is every file in a folder and the folders inside it.</summary>
    public static LibraryKinds Of(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var artPlaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var places = artPlaces.GetAlternateLookup<ReadOnlySpan<char>>();
        var videoFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase).GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var path in paths)
        {
            var kind = MediaFiles.LibraryKindOf(path);
            if (kind is not (LibraryKind.Music or LibraryKind.Video))
            {
                continue;
            }

            var folder = Path.GetDirectoryName(path.AsSpan());
            places.Add(folder);

            // An episode's show takes its art from the folder above its season's too; asked once a folder.
            if (kind == LibraryKind.Video && videoFolders.Add(folder) && ArtworkChoice.Folders(path).Show is { } show)
            {
                artPlaces.Add(show);
            }
        }

        return new LibraryKinds(artPlaces);
    }

    /// <summary>What the library files <paramref name="path"/> as: null for a file that is not media, or is artwork.</summary>
    public LibraryKind? KindOf(string path) =>
        MediaFiles.LibraryKindOf(path) is { } kind && !(kind == LibraryKind.Picture && IsArtwork(path)) ? kind : null;

    /// <summary>Whether <paramref name="path"/> is the artwork of music or video in this listing rather than a picture of its own.</summary>
    public bool IsArtwork(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _artPlaces.Contains(Path.GetDirectoryName(path.AsSpan()))
            && (FolderArt.IsArtName(path) || ArtworkChoice.IsFolderArtName(path));
    }
}
