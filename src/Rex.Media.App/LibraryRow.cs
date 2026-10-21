using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Rex.Media.App;

/// <summary>
/// One item in a library view: a track, video, picture, album, artist, genre, playlist or watched
/// folder (<see cref="Item"/> says which), with artwork once it is loaded.
/// </summary>
public partial class LibraryRow(
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
    private string? _downloadText;
    private bool _isPlaying = isPlaying;
    private bool _summaryOpen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    /// <summary>What the line says under its name; while its file is still downloading, how far that is.</summary>
    public string Detail => _downloadText ?? PlainDetail;

    /// <summary>What the line says under its name once its file is whole.</summary>
    public string PlainDetail { get; } = detail ?? "";

    public string Extra { get; } = extra;

    /// <summary>What the line stands for: a LibraryEntry, LibraryGroup, NamedPlaylist or a folder's path.</summary>
    public object Item { get; } = item;

    public Visibility DetailVisibility => Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The download badge's words ("Downloading · 1.2 GB of 3.4 GB"), empty once the file is whole.</summary>
    public string DownloadText => _downloadText ?? "";

    public Visibility DownloadVisibility => _downloadText is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>How far the download is, 0 to 100, for the badge's ring; 0 while nothing says how big the file will be.</summary>
    public double DownloadPercent { get; private set; }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PictureOpacity)));
        }
    }

    /// <summary>
    /// Artwork fades in over the placeholder instead of popping: 0 until the picture is decoded,
    /// 1 after, animated by the template's composition OpacityTransition.
    /// </summary>
    public double PictureOpacity => _picture is null ? 0 : 1;

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

    /// <summary>
    /// Shows the download badge and detail for a file still arriving (<paramref name="fraction"/>
    /// of it so far, when known), or takes them off with null; nothing is raised when nothing changed.
    /// </summary>
    public void SetDownload(string? text, double? fraction)
    {
        var percent = fraction is { } part ? Math.Round(part * 100) : 0;
        if (text == _downloadText && percent == DownloadPercent)
        {
            return;
        }

        _downloadText = text;
        DownloadPercent = percent;
        foreach (var property in new[] { nameof(DownloadText), nameof(DownloadVisibility), nameof(DownloadPercent), nameof(Detail), nameof(DetailVisibility) })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
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
        PlayingChanged();
    }

    /// <summary>Lets a kind of line say what else changes with the now-playing mark.</summary>
    protected virtual void PlayingChanged()
    {
    }

    /// <summary>Tells the line's template that <paramref name="property"/> changed.</summary>
    protected void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>An episode's synopsis under its line, two lines until it is opened; empty for none.</summary>
    public string Summary { get; private set; } = "";

    public Visibility SummaryVisibility => Summary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>How many lines the synopsis shows: two, or all of it once opened.</summary>
    public int SummaryLines => _summaryOpen ? 0 : 2;

    /// <summary>
    /// Whether the line is a movie's card, which offers its details page. A line has no window to ask the
    /// catalogue answers, so it judges by the film rules alone (title, length and folder).
    /// </summary>
    public Visibility InfoVisibility => Item is Rex.Media.Library.LibraryEntry entry
        && Rex.Media.AppCore.Library.LibraryViews.Movies([entry], Rex.Media.AppCore.Library.MovieRules.Offline).Count == 1
        ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gives the line its synopsis as it is made, before any list shows it.</summary>
    public void SetSummary(string? summary) => Summary = summary ?? "";

    /// <summary>Opens the synopsis to its full length, or folds it back to two lines.</summary>
    public void ToggleSummary()
    {
        _summaryOpen = !_summaryOpen;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SummaryLines)));
    }

    /// <summary>
    /// What a screen reader says for the line, and what the window tests find it by: a list's item
    /// takes its accessible name from here, so it carries the now-playing mark the screen shows.
    /// </summary>
    public override string ToString() => AccessibilityName;
}
