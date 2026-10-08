using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Rex.Media.App;

/// <summary>
/// A line of the library's list: a song or video, an album, artist or genre, a named playlist or a
/// watched folder (<see cref="Item"/> says which), with a picture for videos once one is loaded.
/// </summary>
public sealed partial class LibraryRow(string name, string? detail, string extra, object item, bool hasPicture = false) : INotifyPropertyChanged
{
    private ImageSource? _picture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public string Detail { get; } = detail ?? "";

    public string Extra { get; } = extra;

    /// <summary>What the line stands for: a LibraryEntry, LibraryGroup, NamedPlaylist or a folder's path.</summary>
    public object Item { get; } = item;

    public Visibility DetailVisibility => Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PictureVisibility => HasPicture ? Visibility.Visible : Visibility.Collapsed;

    public bool HasPicture { get; } = hasPicture;

    /// <summary>Whether loading the picture has been started, so it is asked for once.</summary>
    public bool PictureAsked { get; set; }

    public ImageSource? Picture
    {
        get => _picture;
        set
        {
            _picture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Picture)));
        }
    }

    /// <summary>What a screen reader says for the line, and what the window tests find it by.</summary>
    public override string ToString() => Name;
}
