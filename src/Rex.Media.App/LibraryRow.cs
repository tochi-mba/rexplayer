using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Rex.Media.App;

/// <summary>
/// One item in a library view: a track, video, picture, album, artist, genre, playlist or watched
/// folder (<see cref="Item"/> says which), with artwork once it is loaded.
/// </summary>
public sealed partial class LibraryRow(
    string name,
    string? detail,
    string extra,
    object item,
    bool hasPicture = false,
    string picturePlaceholder = "",
    string badge = "",
    double progress = 0) : INotifyPropertyChanged
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

    public string PicturePlaceholder { get; } = picturePlaceholder;

    /// <summary>A compact piece of media-native context: a track number or episode mark.</summary>
    public string Badge { get; } = badge;

    public Visibility BadgeVisibility => Badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>How far through a video left part-way it was stopped, 0 to 1; 0 for the rest.</summary>
    public double Progress { get; } = progress;

    public Visibility ProgressVisibility => Progress > 0 ? Visibility.Visible : Visibility.Collapsed;

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
