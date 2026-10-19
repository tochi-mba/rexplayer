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
    double progress = 0,
    bool isPlaying = false) : INotifyPropertyChanged
{
    private ImageSource? _picture;
    private string _transcriptGlyph = "";
    private string _transcriptText = "";
    private bool _isPlaying = isPlaying;

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

    /// <summary>Whether this row is another representation of the item currently in the player.</summary>
    public bool IsPlaying => _isPlaying;

    public Visibility PlayingVisibility => _isPlaying ? Visibility.Visible : Visibility.Collapsed;

    public string AccessibilityName => _isPlaying ? $"{Name}, now playing" : Name;

    /// <summary>The transcription badge's icon (SUB-20), or empty for none.</summary>
    public string TranscriptGlyph => _transcriptGlyph;

    /// <summary>What the transcription badge says: its tooltip, and the line's help text for a screen reader.</summary>
    public string TranscriptText => _transcriptText;

    public Visibility TranscriptVisibility => _transcriptGlyph.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>Shows a transcription badge; nothing is raised when it is the one shown already.</summary>
    public void SetTranscript(string glyph, string text)
    {
        if (glyph == _transcriptGlyph && text == _transcriptText)
        {
            return;
        }

        (_transcriptGlyph, _transcriptText) = (glyph, text);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranscriptGlyph)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranscriptText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranscriptVisibility)));
    }

    /// <summary>Updates the durable now-playing treatment without rebuilding or moving the view.</summary>
    public void SetPlaying(bool playing)
    {
        if (playing == _isPlaying)
        {
            return;
        }

        _isPlaying = playing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayingVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibilityName)));
    }

    /// <summary>
    /// What a screen reader says for the line, and what the window tests find it by: a list's item
    /// takes its accessible name from here, so it carries the now-playing mark the screen shows.
    /// </summary>
    public override string ToString() => AccessibilityName;
}
