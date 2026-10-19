using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Rex.Media.App;

/// <summary>
/// The library's card templates, compiled with x:Bind. Their pointer and load events go to the
/// window that created the dictionary, which owns the library's picture loading.
/// </summary>
public sealed partial class LibraryCardTemplates : ResourceDictionary
{
    private readonly MainWindow _owner;

    internal LibraryCardTemplates(MainWindow owner)
    {
        _owner = owner;
        InitializeComponent();
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => _owner.OnCardPointerEntered(sender, e);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => _owner.OnCardPointerExited(sender, e);

    private void OnCardGettingFocus(object sender, GettingFocusEventArgs e) => _owner.OnCardGettingFocus(sender, e);

    private void OnCollageItemLoaded(object sender, RoutedEventArgs e) => _owner.OnCollageItemLoaded(sender, e);
}
