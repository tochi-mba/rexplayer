using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Rex.Media.App;

/// <summary>The show page's episode-line template, compiled with x:Bind and created on first use.</summary>
public sealed partial class LibraryEpisodeTemplates : ResourceDictionary
{
    public LibraryEpisodeTemplates()
    {
        InitializeComponent();
    }

    /// <summary>A click on an episode's synopsis opens or folds it, and goes no further: it neither chooses nor plays the line.</summary>
    private void OnSummaryTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            row.ToggleSummary();
            e.Handled = true;
        }
    }
}
