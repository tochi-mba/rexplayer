using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Author rows are navigation, not a selection of their entire catalogue.
    /// Never expose queue, play-next, file deletion or bulk completion actions
    /// when the selected rows stand for authors rather than actual books.
    /// </summary>
    private bool TryAddAuthorNavigationMenu(MenuFlyout menu, IReadOnlyList<LibraryRow> chosen)
    {
        if (_librarySource != LibrarySource.Authors || _libraryGroup is not null)
        {
            return false;
        }

        if (chosen is [{ Item: LibraryGroup }])
        {
            menu.Items.Add(MenuItem("Open author", "LibraryMenuOpenAuthor", () => Activate(chosen[0])));
        }
        else
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Choose one author to open", IsEnabled = false });
        }

        return true;
    }
}
