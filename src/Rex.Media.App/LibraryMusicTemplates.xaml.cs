using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Rex.Media.App;

/// <summary>
/// The music pages' templates, compiled with x:Bind. Their pointer, focus and button events go to
/// the window that created the dictionary, which owns the library's playback and picture loading.
/// </summary>
public sealed partial class LibraryMusicTemplates : ResourceDictionary
{
    private readonly MainWindow _owner;

    internal LibraryMusicTemplates(MainWindow owner)
    {
        _owner = owner;
        InitializeComponent();
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => _owner.OnCardPointerEntered(sender, e);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => _owner.OnCardPointerExited(sender, e);

    private void OnCardGettingFocus(object sender, GettingFocusEventArgs e) => _owner.OnCardGettingFocus(sender, e);

    private void OnCardLosingFocus(object sender, LosingFocusEventArgs e) => _owner.OnCardLosingFocus(sender, e);

    private void OnMusicCardPlay(object sender, RoutedEventArgs e) => MainWindow.OnMusicCardPlay(sender);

    private void OnRowPlay(object sender, RoutedEventArgs e) => _owner.OnMusicRowPlay(sender, e);

    private void OnRowQueue(object sender, RoutedEventArgs e) => _owner.OnMusicRowQueue(sender, e);
}
