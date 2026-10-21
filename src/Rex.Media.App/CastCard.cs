using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Rex.Media.AppCore.Metadata;

namespace Rex.Media.App;

/// <summary>
/// One person in a cast row: their name, who they play and their photo once it is fetched. The
/// photo is asked for when the card is realised and let go when the row recycles it, so only the
/// people on screen hold a picture.
/// </summary>
public sealed partial class CastCard(CastMember member) : INotifyPropertyChanged
{
    private ImageSource? _photo;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The person as the record names them, which a click filters the library by.</summary>
    public CastMember Member { get; } = member;

    public string Name => Member.Name.Trim();

    public string Character => Member.Character?.Trim() ?? "";

    public Visibility CharacterVisibility => Character.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The letters shown until the photo comes, or for good when there is none.</summary>
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpperInvariant(word[0])));

    /// <summary>The photo's request while it is under way, cancelled when the card leaves the row.</summary>
    internal CancellationTokenSource? PhotoWork { get; set; }

    public ImageSource? Photo
    {
        get => _photo;
        set
        {
            _photo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Photo)));
        }
    }

    /// <summary>Cancels the photo's request and lets the decoded picture go.</summary>
    internal void Release()
    {
        PhotoWork?.Cancel();
        PhotoWork?.Dispose();
        PhotoWork = null;
        Photo = null;
    }

    /// <summary>What a screen reader says, and what the window tests find the card by.</summary>
    public override string ToString() => Name;
}
