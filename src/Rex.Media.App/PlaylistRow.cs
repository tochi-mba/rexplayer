using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rex.Media.Library;
using Rex.Media.AppCore.Player;

namespace Rex.Media.App;

/// <summary>
/// A line of the playlist: its picture, title and context, marked when its file is missing
/// (LIB-09). Two lines are the same when they say the same of the same file, so a playlist that
/// redraws keeps its lines, and their pictures.
/// </summary>
public sealed partial class PlaylistRow(string title, string? secondary, bool current, bool missing, string location, TimeSpan? duration = null) : INotifyPropertyChanged, IEquatable<PlaylistRow>
{
    // Segoe Fluent Icons: Audio, Photo and Video, shown until the line's picture comes.
    private static readonly string MusicGlyph = char.ConvertFromUtf32(0xE8D6);
    private static readonly string PictureGlyph = char.ConvertFromUtf32(0xEB9F);
    private static readonly string VideoGlyph = char.ConvertFromUtf32(0xE714);

    private ImageSource? _picture;
    private double _progressPercent;
    private string? _state;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; } = title;

    public string? Secondary { get; } = secondary;

    public bool Current { get; } = current;

    public bool Missing { get; } = missing;

    public string Location { get; } = location;

    /// <summary>Reliable queue duration, or empty rather than a guessed time.</summary>
    public string DurationText { get; } = duration is { Ticks: > 0 } known ? TimeText.Format(known, known) : "";

    /// <summary>The duration's place says "Missing" instead for a file that is gone.</summary>
    public Visibility DurationVisibility => Missing || DurationText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// The playing line's progress, once it shows (half a percent is about a pixel): an empty bar
    /// reads as a stray line under the row.
    /// </summary>
    public Visibility ProgressVisibility => Current && DurationText.Length > 0 && ProgressShows(_progressPercent) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The playing row's position. UI updates never mutate the queue or restart artwork loads.</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (Math.Abs(_progressPercent - value) < 0.05)
            {
                return;
            }

            var shown = ProgressShows(_progressPercent);
            _progressPercent = value;
            Raise(nameof(ProgressPercent));
            if (shown != ProgressShows(value))
            {
                Raise(nameof(ProgressVisibility));
            }
        }
    }

    public void SetProgress(TimeSpan position, TimeSpan duration)
    {
        if (!Current || duration <= TimeSpan.Zero)
        {
            return;
        }

        ProgressPercent = Math.Clamp(position.TotalSeconds / duration.TotalSeconds * 100, 0, 100);
    }

    /// <summary>"Playing" or "Paused" on the current line while the player holds it (QueuePresentation decides), else nothing.</summary>
    public string State => _state ?? "";

    public Visibility StateVisibility => _state is null ? Visibility.Collapsed : Visibility.Visible;

    public void SetState(string? state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        Raise(nameof(State));
        Raise(nameof(StateVisibility));
        Raise(nameof(AccessibleName));
    }

    /// <summary>What kind of media the line is, by its name: music, a video or a picture (video for a stream).</summary>
    public LibraryKind Kind { get; } = Rex.Media.AppCore.Player.MediaFiles.LibraryKindOf(location) ?? LibraryKind.Video;

    /// <summary>A missing file's picture is faded; its words keep their contrast, and its status says what is wrong.</summary>
    public double PictureOpacity => Missing ? 0.5 : 1;

    public Visibility MissingVisibility => Missing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SecondaryVisibility => string.IsNullOrWhiteSpace(Secondary) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CurrentVisibility => Current ? Visibility.Visible : Visibility.Collapsed;

    public string AccessibleName => string.Join(". ", new[]
    {
        _state,
        Title,
        Secondary,
        Missing ? "Missing" : null,
    }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>The icon shown until the picture comes.</summary>
    public string PlaceholderGlyph => Kind switch { LibraryKind.Music => MusicGlyph, LibraryKind.Picture => PictureGlyph, _ => VideoGlyph };

    /// <summary>Whether loading the picture has been started, so it is asked for once.</summary>
    public bool PictureAsked { get; set; }

    public ImageSource? Picture
    {
        get => _picture;
        set
        {
            _picture = value;
            Raise(nameof(Picture));
        }
    }

    public bool Equals(PlaylistRow? other) => other is not null && other.Title == Title && other.Secondary == Secondary && other.Current == Current && other.Missing == Missing && other.Location == Location && other.DurationText == DurationText;

    public override bool Equals(object? obj) => Equals(obj as PlaylistRow);

    public override int GetHashCode() => HashCode.Combine(Title, Secondary, Current, Missing, Location, DurationText);

    /// <summary>What a screen reader says for the line.</summary>
    public override string ToString() => AccessibleName;

    private static bool ProgressShows(double percent) => percent >= 0.5;

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
