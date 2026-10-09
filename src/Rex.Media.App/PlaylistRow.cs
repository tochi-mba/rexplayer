using Microsoft.UI.Xaml;

namespace Rex.Media.App;

/// <summary>A line of the playlist: its text, greyed and marked when its file is missing (LIB-09).</summary>
public sealed record PlaylistRow(string Text, bool Missing)
{
    public double Opacity => Missing ? 0.5 : 1;

    public Visibility MissingVisibility => Missing ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>What a screen reader says for the line.</summary>
    public override string ToString() => Missing ? Text + " (missing)" : Text;
}
