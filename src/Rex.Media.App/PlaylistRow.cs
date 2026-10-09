using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rex.Media.Library;

namespace Rex.Media.App;

/// <summary>
/// A line of the playlist: its picture, its text, greyed and marked when its file is missing
/// (LIB-09). Two lines are the same when they say the same of the same file, so a playlist that
/// redraws keeps its lines, and their pictures.
/// </summary>
public sealed partial class PlaylistRow(string text, bool missing, string location) : INotifyPropertyChanged, IEquatable<PlaylistRow>
{
    private ImageSource? _picture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text { get; } = text;

    public bool Missing { get; } = missing;

    public string Location { get; } = location;

    /// <summary>What kind of media the line is, by its name: music, a video or a picture (video for a stream).</summary>
    public LibraryKind Kind { get; } = Rex.Media.AppCore.Player.MediaFiles.LibraryKindOf(location) ?? LibraryKind.Video;

    public double Opacity => Missing ? 0.5 : 1;

    public Visibility MissingVisibility => Missing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The mark shown until the picture comes.</summary>
    public string Placeholder => Kind switch { LibraryKind.Music => "♪", LibraryKind.Picture => "▧", _ => "▶" };

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

    public bool Equals(PlaylistRow? other) => other is not null && other.Text == Text && other.Missing == Missing && other.Location == Location;

    public override bool Equals(object? obj) => Equals(obj as PlaylistRow);

    public override int GetHashCode() => HashCode.Combine(Text, Missing, Location);

    /// <summary>What a screen reader says for the line.</summary>
    public override string ToString() => Missing ? Text + " (missing)" : Text;
}
