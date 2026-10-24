using Microsoft.UI.Xaml;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>A card shows its play mark while pointed at or keyboard-focused.</summary>
    internal void OnCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetCardPlay(sender, 1);
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            PrioritizeLibraryPicture(row);
        }
    }

    internal void OnCardGettingFocus(object sender, Microsoft.UI.Xaml.Input.GettingFocusEventArgs e)
    {
        SetCardPlay(sender, 1);
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            PrioritizeLibraryPicture(row);
        }
    }

    internal void OnCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SetCardPlay(sender, 0);

    internal void OnCardLosingFocus(object sender, Microsoft.UI.Xaml.Input.LosingFocusEventArgs e) => SetCardPlay(sender, 0);

    /// <summary>
    /// Pointing at a card, or focusing it with the keyboard, reveals its play mark and lifts it a
    /// little. Both are composition transitions the GPU runs on its own: no timers, no per-frame code.
    /// </summary>
    private static void SetCardPlay(object card, double opacity)
    {
        if (card is not FrameworkElement element)
        {
            return;
        }

        if (element.FindName("CardPlay") is UIElement play)
        {
            play.OpacityTransition ??= new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
            play.Opacity = opacity;
        }

        element.ScaleTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(140) };
        element.CenterPoint = new System.Numerics.Vector3((float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0);
        element.Scale = opacity > 0 ? new System.Numerics.Vector3(1.03f, 1.03f, 1f) : System.Numerics.Vector3.One;
    }
}
