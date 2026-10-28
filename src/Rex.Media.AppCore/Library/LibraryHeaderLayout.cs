namespace Rex.Media.AppCore.Library;

/// <summary>
/// How much of the library its header takes (LIB-05). A short library gets the compact header on
/// every view from the moment the view opens, so one window never shows a full banner on one page
/// and a bare title on the next; scrolling a view compacts the header too, and its top restores
/// it. A page without a banner of its own (a Browse page, the music Home) still shows its name, as
/// a compact header. A compact header stays compact until the view is back at its very top, so a
/// view barely taller than its viewport cannot bounce between the two as the header's height changes
/// what it can scroll. The search box belongs to views, so a page about one thing (an album, a show,
/// a movie) leaves it out unless something is already typed in it. The compact header puts its
/// actions beside the title when both fit, and under it when not; the Browse billboard leaves the
/// first shelf in view under it.
/// </summary>
public static class LibraryHeaderLayout
{
    /// <summary>A library shorter than this, in pixels, gets the compact header.</summary>
    public const double ShortLibrary = 550;

    /// <summary>A library shorter than this drops Home's search and view options, so its shelves keep the room.</summary>
    public const double TinyLibrary = 320;

    /// <summary>A view scrolled further than this compacts the header.</summary>
    public const double CollapseOffset = 28;

    /// <summary>The least room the compact header's title keeps beside its actions.</summary>
    public const double TitleRoom = 160;

    /// <summary>The billboard's height when there is room for it.</summary>
    public const double TallBillboard = 320;

    /// <summary>The least height a billboard is given, which its words need.</summary>
    public const double ShortBillboard = 248;

    /// <summary>What the first shelf shows under the billboard: its heading and the top of its cards.</summary>
    public const double ShelfPeek = 160;

    /// <summary>
    /// Whether the header is compact: always for a page whose header is only its name, else when the
    /// library is short (a height of 0 is not laid out yet) or the view is scrolled from its top:
    /// past <see cref="CollapseOffset"/> to compact it, and back to the top to expand it again.
    /// </summary>
    public static bool Compact(double libraryHeight, double scrolled, bool titleOnly, bool compactNow) =>
        titleOnly || libraryHeight is > 0 and < ShortLibrary || scrolled > (compactNow ? 0 : CollapseOffset);

    /// <summary>Whether the search box and view options are shown: everywhere but Home in a tiny library.</summary>
    public static bool OffersViewOptions(bool home, double libraryHeight) => !home || libraryHeight is <= 0 or >= TinyLibrary;

    /// <summary>Whether the search box is shown: on views, and on a page about one thing only while it holds a search.</summary>
    public static bool OffersSearch(bool onePage, string typed) => !onePage || typed.Trim().Length > 0;

    /// <summary>Whether the compact header's actions fit beside its title in <paramref name="room"/> pixels.</summary>
    public static bool ActionsBesideTitle(double room, double actions) => room - actions >= TitleRoom;

    /// <summary>
    /// The billboard's height over shelves <paramref name="shelves"/> pixels tall: tall when there is
    /// room, else short enough for the first shelf to show; tall while the shelves are not laid out.
    /// </summary>
    public static double BillboardHeight(double shelves) =>
        shelves <= 0 ? TallBillboard : Math.Clamp(shelves - ShelfPeek, ShortBillboard, TallBillboard);
}
