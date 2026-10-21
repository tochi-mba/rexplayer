using Rex.Media.Settings;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// Where the user was in the library, so that coming back to a view, a page or a search finds it
/// as it was left and nobody has to scroll to the same place twice (LIB-05).
///
/// A place is kept per route: the view (Songs, Albums, Home...), a group opened in it (an album,
/// an artist, a show), the group's page (a season, an artist's album) and the search typed. The
/// routes nest, so Back from a page finds the parent's own place, with the line that opened the
/// page chosen. Places outlive a restart through the settings; the last search of each view lasts
/// the session only. What a place holds is <see cref="LibraryPlace"/>; how the window measures and
/// applies it is the window's business.
/// </summary>
public sealed class LibraryPlaces
{
    private const char Part = '\n';
    private const string SearchMark = "\n?";
    private readonly OrderedDictionary<string, LibraryPlace> _places;
    private readonly Dictionary<string, string> _searches = new(StringComparer.Ordinal);

    public LibraryPlaces(IReadOnlyDictionary<string, LibraryPlace>? kept = null)
    {
        _places = new OrderedDictionary<string, LibraryPlace>(StringComparer.Ordinal);
        foreach (var (key, place) in kept ?? new Dictionary<string, LibraryPlace>())
        {
            Remember(key, place);
        }
    }

    /// <summary>
    /// The key of a route: the view, the group opened in it (with the detail that tells two
    /// albums of one name apart), the group's page, and, apart from them, the search.
    /// </summary>
    public static string Key(string view, string? group = null, string? groupDetail = null, string? page = null, string search = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        var key = view;
        if (!string.IsNullOrEmpty(group))
        {
            key += Part + group + Part + groupDetail;
            if (!string.IsNullOrEmpty(page))
            {
                key += Part + page;
            }
        }

        return string.IsNullOrWhiteSpace(search) ? key : key + SearchMark + search.Trim();
    }

    /// <summary>The key of <paramref name="route"/> as a view with nothing searched.</summary>
    public static string Key(LibraryRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return Key(route.View, route.Group, route.GroupDetail, route.Page);
    }

    /// <summary>Whether <paramref name="key"/> names a search; its places last the session only.</summary>
    public static bool IsSearch(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Contains(SearchMark, StringComparison.Ordinal);
    }

    /// <summary>The key of the same route with its search cleared.</summary>
    public static string Unsearched(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var mark = key.IndexOf(SearchMark, StringComparison.Ordinal);
        return mark < 0 ? key : key[..mark];
    }

    /// <summary>
    /// Keeps <paramref name="place"/> for <paramref name="key"/>, newest last; an empty place
    /// forgets the key. True when something changed and the settings should be written.
    /// </summary>
    public bool Remember(string key, LibraryPlace place)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(place);
        var normalized = place.Normalize();
        if (normalized.IsEmpty)
        {
            return _places.Remove(key);
        }

        if (_places.TryGetValue(key, out var kept) && kept == normalized)
        {
            return false;
        }

        _places.Remove(key);
        _places[key] = normalized;
        return true;
    }

    /// <summary>The place kept for <paramref name="key"/>, or null for the top of the view.</summary>
    public LibraryPlace? PlaceOf(string key) => _places.TryGetValue(key, out var place) ? place : null;

    /// <summary>Forgets every place; the library was changed under them (a restore from a backup).</summary>
    public void Forget()
    {
        _places.Clear();
        _searches.Clear();
    }

    /// <summary>Keeps the search typed in the view <paramref name="viewKey"/> for the session; blank forgets it.</summary>
    public void RememberSearch(string viewKey, string search)
    {
        ArgumentException.ThrowIfNullOrEmpty(viewKey);
        if (string.IsNullOrWhiteSpace(search))
        {
            _searches.Remove(viewKey);
        }
        else
        {
            _searches[viewKey] = search;
        }
    }

    /// <summary>The search last typed in the view <paramref name="viewKey"/> this session, or empty.</summary>
    public string SearchOf(string viewKey) => _searches.TryGetValue(viewKey, out var search) ? search : "";

    /// <summary>The places to keep in the settings: the newest <see cref="LibraryPlace.Most"/>, searches left out.</summary>
    public IReadOnlyDictionary<string, LibraryPlace> Snapshot() =>
        _places.Where(pair => !IsSearch(pair.Key)).TakeLast(LibraryPlace.Most).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// The line to keep in view when a view is built again around the same things (a scan found
    /// more, the order or a filter changed): the first of <paramref name="before"/> (the lines
    /// chosen, then the line at the top) still among <paramref name="after"/>, or null when none
    /// survived and the top is the only place left.
    /// </summary>
    public static string? SurvivingAnchor(IEnumerable<string?> before, IReadOnlySet<string> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return before.FirstOrDefault(identity => identity is not null && after.Contains(identity));
    }
}
