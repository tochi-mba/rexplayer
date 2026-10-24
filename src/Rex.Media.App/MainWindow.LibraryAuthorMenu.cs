using Microsoft.UI.Xaml.Controls;
using Rex.Media.AppCore.Library;

namespace Rex.Media.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Author rows navigate, and book cards must never become a mixed-file bulk operation.
    /// Never expose queue, play-next, file deletion or bulk completion actions
    /// when the selected rows stand for authors rather than actual books.
    /// </summary>
    private bool TryAddScopedAudiobookMenu(MenuFlyout menu, IReadOnlyList<LibraryRow> chosen)
    {
        // Multi-selection of book cards is never a file-level bulk operation:
        // generic Play next, Enqueue and Delete would flatten several books.
        // A single book still exposes its normal scoped commands.
        var bookCards = chosen.Any(row => row.Item is LibraryGroup);
        var onBookShelf = (_librarySource == LibrarySource.Audiobooks && _libraryGroup is null)
            || (_librarySource == LibrarySource.Authors && _libraryGroup is not null && _librarySeason is null)
            || _librarySource == LibrarySource.Completed;
        if (onBookShelf && chosen.Count > 1 && bookCards)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "Choose one book to act on",
                IsEnabled = false,
            });
            return true;
        }

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
