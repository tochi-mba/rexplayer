using System.Globalization;
using System.Text.RegularExpressions;
using Rex.Media.AppCore.Player;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// The audio that is not music, shelf by shelf: the books (each gathered from its parts), the podcast
/// shows (each with its episodes), the recordings (newest first) and the sounds.
/// </summary>
public sealed record ListeningShelves(
    IReadOnlyList<LibraryGroup> Audiobooks,
    IReadOnlyList<LibraryGroup> Podcasts,
    IReadOnlyList<LibraryEntry> Recordings,
    IReadOnlyList<LibraryEntry> Sounds);

/// <summary>One chapter of a book: in which part it is, where in that part it starts, and its title.</summary>
public sealed record BookChapter(LibraryEntry Part, TimeSpan Start, string Title);

/// <summary>Where a book is up to: which of its parts, and how far into it.</summary>
public readonly record struct BookPlace(int Part, TimeSpan At);

/// <summary>Something part-way through: a book (<see cref="Book"/>, with its part to go on with) or a podcast episode.</summary>
public sealed record InProgress(LibraryEntry Entry, LibraryGroup? Book);

/// <summary>
/// How the Audiobooks, Podcasts and Recordings views are arranged (<see cref="AudioRules"/> decides
/// what is in each). A book is its album tag, its folder (a "Disc 2" or "CD1" folder is part of the
/// book above it) and its author, so several books in one folder part by album, and "Part 1",
/// "Part 2" and the rest come together as one book with one place to resume from. A show is its
/// album tag, else its artist, else its folder, compared without case or punctuation, so one show
/// kept in two folders is one show.
/// </summary>
public static partial class ListeningViews
{
    public const string UnknownShow = "Unknown show";
    public const string WhatsAppHeader = "WhatsApp";
    public const string SoundsHeader = "Sounds";
    public const string UndatedHeader = "Undated";

    /// <summary>How long an unplayed episode counts as new, for Home's "New episodes".</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(14);

    private static readonly StringComparer ByName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

    /// <summary>The audio among <paramref name="entries"/> that is not music, shelf by shelf.</summary>
    public static ListeningShelves Arrange(IReadOnlyCollection<LibraryEntry> entries, Func<LibraryEntry, MusicLookup> lookup,
        Func<LibraryGroup, string?>? unknownAuthor = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(lookup);
        var context = AudioContext.Of(entries);
        var shelves = entries.Select(entry => (Entry: entry, Verdict: AudioRules.Judge(entry, lookup(entry), context)))
            .Where(pair => pair.Verdict is { IsMusic: false })
            .ToLookup(pair => pair.Verdict!.Value.Shelf, pair => pair.Entry);
        return new ListeningShelves(
            Books(shelves[AudioShelf.Audiobooks], unknownAuthor),
            Shows(shelves[AudioShelf.Podcasts]),
            [.. shelves[AudioShelf.Recordings].OrderByDescending(entry => RecordedAt(entry) ?? DateTime.MinValue).ThenBy(entry => entry.Path, NaturalOrder.Instance)],
            [.. shelves[AudioShelf.Sounds].OrderBy(entry => entry.Title, NaturalOrder.Instance).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The books <paramref name="parts"/> make, by name, each with its parts in reading order and its author as the detail.</summary>
    public static IReadOnlyList<LibraryGroup> Books(IEnumerable<LibraryEntry> parts,
        Func<LibraryGroup, string?>? unknownAuthor = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return [.. parts
            .GroupBy(part => (Name: BookCredits.Title(BookNameOf(part)), Folder: BookFolderOf(part)), BookKeyComparer.Instance)
            .SelectMany(partsOfTitle =>
            {
                var credited = partsOfTitle.Select(part => (Part: part, Author: BookCredits.Author(part))).ToList();
                var authors = credited.Select(credit => credit.Author).OfType<string>().Distinct(ByName).ToList();
                // Missing credits on chapters join a single known writer; conflicts remain separate.
                // Never silently give an uncredited chapter to one of two disagreeing writers.
                var unambiguous = authors.Count == 1 ? authors[0]
                    : authors.Count == 0 && unknownAuthor is not null
                        ? unknownAuthor(new LibraryGroup(partsOfTitle.Key.Name, null, [.. partsOfTitle])) : null;
                return credited.GroupBy(credit => credit.Author ?? unambiguous ?? "", ByName)
                    .Select(book => new LibraryGroup(partsOfTitle.Key.Name, book.Key.Length > 0 ? book.Key : null,
                        [.. book.Select(credit => credit.Part).OrderBy(DiscOf)
                            .ThenBy(part => part.Track ?? int.MaxValue).ThenBy(part => part.Path, NaturalOrder.Instance)]));
            })
            .OrderBy(book => book.Name, ByName)
            .ThenBy(book => book.Detail ?? "", ByName)];
    }

    public const string UnknownAuthor = "Unknown author";

    /// <summary>
    /// One author card per distinct credited author, holding their books' parts. Unknown authors
    /// stay separate from real authors and are displayed after them.
    /// </summary>
    public static IReadOnlyList<LibraryGroup> Authors(IEnumerable<LibraryGroup> books)
    {
        ArgumentNullException.ThrowIfNull(books);
        return [.. books
            .GroupBy(book => string.IsNullOrWhiteSpace(book.Detail) ? UnknownAuthor : book.Detail.Trim(), ByName)
            .Select(group => new LibraryGroup(group.Key, null,
                [.. group.SelectMany(book => book.Entries).DistinctBy(part => part.Path, StringComparer.OrdinalIgnoreCase)]))
            .OrderBy(author => author.Name == UnknownAuthor)
            .ThenBy(author => author.Name, ByName)];
    }

    /// <summary>The specific books credited to an author, without joining chapters from different titles.</summary>
    public static IReadOnlyList<LibraryGroup> BooksByAuthor(IEnumerable<LibraryGroup> books, string author)
    {
        ArgumentNullException.ThrowIfNull(books);
        ArgumentNullException.ThrowIfNull(author);
        return [.. books.Where(book => ByName.Equals(string.IsNullOrWhiteSpace(book.Detail) ? UnknownAuthor : book.Detail.Trim(), author))
            .OrderBy(book => book.Name, ByName)];
    }

    /// <summary>The shows <paramref name="episodes"/> belong to, by name, each with its episodes newest first and its host as the detail.</summary>
    public static IReadOnlyList<LibraryGroup> Shows(IEnumerable<LibraryEntry> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        return [.. episodes
            .GroupBy(episode => Normalized(ShowNameOf(episode)), StringComparer.Ordinal)
            .Select(show => new LibraryGroup(MostCommon(show.Select(ShowNameOf))!, MostCommon(show.Where(episode => episode.Album is not null).Select(episode => episode.Artist)),
                Episodes([.. show], oldestFirst: false)))
            .OrderBy(show => show.Name, ByName)];
    }

    /// <summary>A show's episodes newest first, or oldest first; ties by episode number, then name.</summary>
    public static IReadOnlyList<LibraryEntry> Episodes(IReadOnlyList<LibraryEntry> episodes, bool oldestFirst)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var newestFirst = episodes.OrderByDescending(EpisodeDate).ThenByDescending(episode => episode.Track ?? 0).ThenByDescending(episode => episode.Path, NaturalOrder.Instance).ToList();
        if (oldestFirst)
        {
            newestFirst.Reverse();
        }

        return newestFirst;
    }

    /// <summary>
    /// When an episode came out: its year tag (the file's date when that is in the same year), else the
    /// file's date, else a date in its name; the earliest date there is when nothing says.
    /// </summary>
    public static DateTime EpisodeDate(LibraryEntry episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        if (episode.Year is { } year)
        {
            return episode.Modified.Year == year ? episode.Modified : new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        return episode.Modified != default ? episode.Modified : DateInName(MediaLibrary.TitleOf(episode.Path)) ?? DateTime.MinValue;
    }

    /// <summary>The show's newest episode's date, for its card.</summary>
    public static DateTime NewestEpisode(LibraryGroup show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return show.Entries.Max(EpisodeDate);
    }

    /// <summary>How many of a show's episodes are unplayed: never played, and not left part-way.</summary>
    public static int Unplayed(LibraryGroup show, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(leftAt);
        return show.Entries.Count(episode => IsUnplayed(episode, leftAt));
    }

    /// <summary>Whether an episode is unplayed: never played, and not left part-way.</summary>
    public static bool IsUnplayed(LibraryEntry episode, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(leftAt);
        return episode.Plays == 0 && leftAt(episode.Path) is null;
    }

    /// <summary>"Play unplayed in order": the show's unplayed episodes, oldest first.</summary>
    public static IReadOnlyList<LibraryEntry> UnplayedInOrder(LibraryGroup show, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(leftAt);
        return [.. Episodes(show.Entries, oldestFirst: true).Where(episode => IsUnplayed(episode, leftAt))];
    }

    /// <summary>What a book is known by in the person's own records (its speed, "Mark as finished"): its name and author, however cased.</summary>
    public static string BookKey(LibraryGroup book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return (book.Name + "\n" + book.Detail).ToUpperInvariant();
    }

    /// <summary>A book's chapters: those its parts mark (an .m4b's chapter atoms), and a part with none is one chapter by itself.</summary>
    public static IReadOnlyList<BookChapter> Chapters(LibraryGroup book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return [.. book.Entries.SelectMany(part => part.Chapters is { Count: > 0 } marked
            ? marked.Select(chapter => new BookChapter(part, chapter.Start, chapter.Title.Length > 0 ? chapter.Title : part.Title))
            : [new BookChapter(part, TimeSpan.Zero, part.Title)])];
    }

    /// <summary>Where the book is up to: the part played last, at the point it was left (its start when there is none); the first part for a book never played.</summary>
    public static BookPlace PlaceIn(LibraryGroup book, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(leftAt);
        var latest = 0;
        for (var index = 1; index < book.Entries.Count; index++)
        {
            if (book.Entries[index].LastPlayed is { } played && played >= (book.Entries[latest].LastPlayed ?? DateTime.MinValue))
            {
                latest = index;
            }
        }

        return new BookPlace(latest, leftAt(book.Entries[latest].Path) ?? TimeSpan.Zero);
    }

    /// <summary>Whether the book has been started: a part played, or left part-way.</summary>
    public static bool Started(LibraryGroup book, Func<string, TimeSpan?> leftAt)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(leftAt);
        return book.Entries.Any(part => part.LastPlayed is not null || leftAt(part.Path) is not null);
    }

    /// <summary>How far through the book <paramref name="place"/> is, 0 to 1, by the parts' running times (by parts while those are unknown).</summary>
    public static double Progress(LibraryGroup book, BookPlace place)
    {
        ArgumentNullException.ThrowIfNull(book);
        var total = book.Entries.Sum(part => (part.Duration ?? TimeSpan.Zero).Ticks);
        if (total == 0)
        {
            return Math.Clamp((double)place.Part / book.Entries.Count, 0, 1);
        }

        var before = book.Entries.Take(place.Part).Sum(part => (part.Duration ?? TimeSpan.Zero).Ticks);
        return Math.Clamp((before + place.At.Ticks) / (double)total, 0, 1);
    }

    /// <summary>
    /// "Continue listening": the books started and not finished, at the part to go on with, and the
    /// podcast episodes left part-way, the one listened to last first.
    /// </summary>
    public static IReadOnlyList<InProgress> ContinueListening(ListeningShelves shelves, Func<string, TimeSpan?> leftAt, Func<LibraryGroup, bool> finished)
    {
        ArgumentNullException.ThrowIfNull(shelves);
        ArgumentNullException.ThrowIfNull(leftAt);
        ArgumentNullException.ThrowIfNull(finished);
        var books = shelves.Audiobooks.Where(book => Started(book, leftAt) && !finished(book))
            .Select(book => new InProgress(book.Entries[PlaceIn(book, leftAt).Part], book));
        var episodes = shelves.Podcasts.SelectMany(show => show.Entries).Where(episode => leftAt(episode.Path) is not null)
            .Select(episode => new InProgress(episode, null));
        return [.. books.Concat(episodes)
            .OrderByDescending(item => item.Book?.Entries.Max(part => part.LastPlayed) ?? item.Entry.LastPlayed)
            .ThenBy(item => item.Entry.Path, NaturalOrder.Instance)];
    }

    /// <summary>Home's "New episodes": unplayed episodes added in the last <see cref="NewFor"/>, newest first.</summary>
    public static IReadOnlyList<LibraryEntry> NewEpisodes(ListeningShelves shelves, Func<string, TimeSpan?> leftAt, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(shelves);
        ArgumentNullException.ThrowIfNull(leftAt);
        return [.. shelves.Podcasts.SelectMany(show => show.Entries)
            .Where(episode => IsUnplayed(episode, leftAt) && nowUtc - episode.Added <= NewFor)
            .OrderByDescending(EpisodeDate).ThenBy(episode => episode.Path, NaturalOrder.Instance)];
    }

    /// <summary>When a recording was made: the date and time in its name, else the file's date; null when neither says.</summary>
    public static DateTime? RecordedAt(LibraryEntry recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return DateInName(MediaLibrary.TitleOf(recording.Path)) ?? (recording.Modified != default ? recording.Modified : null);
    }

    /// <summary>
    /// The Recordings view's headings, newest first: Today, Yesterday, This week, then each month; the
    /// messenger's voice notes under WhatsApp; recordings with no date under Undated; the sounds last.
    /// </summary>
    public static IReadOnlyList<LibrarySection> RecordingSections(IReadOnlyList<LibraryEntry> recordings, IReadOnlyList<LibraryEntry> sounds, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(sounds);
        var today = nowLocal.Date;
        string HeaderOf(LibraryEntry recording)
        {
            if (WhatsAppPattern().IsMatch(MediaLibrary.TitleOf(recording.Path)))
            {
                return WhatsAppHeader;
            }

            if (RecordedAt(recording) is not { } at)
            {
                return UndatedHeader;
            }

            var day = (at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at).Date;
            return day == today ? "Today"
                : day == today.AddDays(-1) ? "Yesterday"
                : day > today.AddDays(-7) && day <= today ? "This week"
                : day.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        }

        var sections = recordings
            .OrderByDescending(recording => RecordedAt(recording) ?? DateTime.MinValue).ThenBy(recording => recording.Path, NaturalOrder.Instance)
            .GroupBy(HeaderOf)
            .OrderBy(section => section.Key == UndatedHeader ? 2 : section.Key == WhatsAppHeader ? 1 : 0)
            .Select(section => new LibrarySection(section.Key, [.. section]))
            .ToList();
        if (sounds.Count > 0)
        {
            sections.Add(new LibrarySection(SoundsHeader, sounds));
        }

        return sections;
    }

    /// <summary>The book a part belongs to: its album, else its folder (above a disc or part folder), else its title without a part number.</summary>
    private static string BookNameOf(LibraryEntry part)
    {
        if (part.Album is { } album)
        {
            return album;
        }

        var folder = Path.GetFileName(BookFolderOf(part));
        if (LooseFolderPattern().IsMatch(folder)
            || (BookCredits.AuthorFolder(part) is { } writer && ByName.Equals(writer, folder)))
        {
            // In Audiobooks/Author/Title.m4b the author directory is not the book title.
            return part.Title;
        }

        return folder.Length > 0 && !folder.EndsWith(':') && !BooksFolderPattern().IsMatch(folder) ? folder
            : PartNumberPattern().Replace(part.Title, "") is { Length: > 0 } title ? title : part.Title;
    }

    /// <summary>The folder a book's parts share: a part's own, or the one above a "Disc 2" or "CD1" folder.</summary>
    private static string BookFolderOf(LibraryEntry part)
    {
        var folder = AudioRules.FolderOf(part.Path);
        return PartFolderPattern().IsMatch(Path.GetFileName(folder)) ? AudioRules.FolderOf(folder) : folder;
    }

    /// <summary>Which disc a part is on: its disc tag, else the number of its "Disc 2" or "CD1" folder, else 0.</summary>
    private static int DiscOf(LibraryEntry part) =>
        part.Disc ?? (PartFolderPattern().Match(Path.GetFileName(AudioRules.FolderOf(part.Path))) is { Success: true } folder
            ? int.Parse(folder.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture) : 0);

    /// <summary>The show an episode belongs to: its album, else its artist, else its folder.</summary>
    private static string ShowNameOf(LibraryEntry episode) =>
        episode.Album ?? episode.Artist ?? episode.AlbumArtist
        ?? (Path.GetFileName(AudioRules.FolderOf(episode.Path)) is { Length: > 0 } folder && !folder.EndsWith(':') && !ShowsFolderPattern().IsMatch(folder) ? folder : UnknownShow);

    /// <summary>A name without case, accents' companions, spaces or punctuation: "The Daily" and "the-daily" are one.</summary>
    private static string Normalized(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    /// <summary>A date (and time) in a name: "20240105_101010", "2024-01-05 10-00-00", "AUD-20240105-WA0001", "2024-01-05".</summary>
    private static DateTime? DateInName(string name)
    {
        var match = DatePattern().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var text = match.Groups["y"].Value + match.Groups["mo"].Value + match.Groups["d"].Value
            + (match.Groups["h"].Success ? match.Groups["h"].Value + match.Groups["mi"].Value + (match.Groups["s"].Success ? match.Groups["s"].Value : "00") : "000000");
        return DateTime.TryParseExact(text, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at) ? at : null;
    }

    /// <summary>The value most of the files give, the first on a tie; null when none gives one.</summary>
    private static string? MostCommon(IEnumerable<string?> values) =>
        values.OfType<string>().GroupBy(value => value, ByName).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();

    /// <summary>A date, then perhaps a time, in a name.</summary>
    [GeneratedRegex(@"(?<!\d)(?<y>(?:19|20)\d{2})[\-_.]?(?<mo>[01]\d)[\-_.]?(?<d>[0-3]\d)(?:[ _\-T](?<h>[0-2]\d)[\-_.:]?(?<mi>[0-5]\d)(?:[\-_.:]?(?<s>[0-5]\d))?)?(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    /// <summary>A messenger's voice note or audio: "PTT-20240105-WA0003", "AUD-20240105-WA0001".</summary>
    [GeneratedRegex(@"^(?:PTT|AUD)-\d{8}-WA\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhatsAppPattern();

    /// <summary>A folder holding one part of a book: "Disc 2", "CD1", "Part 3", "Disk_04".</summary>
    [GeneratedRegex(@"^(?:dis[ck]|cd|part)[ _\-]*(?<n>\d{1,6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartFolderPattern();

    /// <summary>The generic book collection root: not the title of a book.</summary>
    [GeneratedRegex(@"^(?:audio ?books?|books)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BooksFolderPattern();

    /// <summary>A loose-download folder never serves as a book's title.</summary>
    [GeneratedRegex(@"^(?:downloads?|inbox|incoming)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LooseFolderPattern();

    /// <summary>The folder every show is in, which names none of them: "Podcasts".</summary>
    [GeneratedRegex(@"^podcasts?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShowsFolderPattern();

    /// <summary>A part's number at the end of its title: " - Part 01", " (Disc 2)", " CD3", ", Chapter 4", " 07", " Part 3 of 12".</summary>
    [GeneratedRegex(@"[ _\-,.(\[]*(?:(?:part|pt|dis[ck]|cd|chapter|ch)[ _\-.]*)?\d+(?:[ _\-]*of[ _\-]*\d+)?[)\]]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartNumberPattern();

    /// <summary>Books' keys compared without case.</summary>
    private sealed class BookKeyComparer : IEqualityComparer<(string Name, string Folder)>
    {
        public static readonly BookKeyComparer Instance = new();

        public bool Equals((string Name, string Folder) x, (string Name, string Folder) y) =>
            ByName.Equals(x.Name, y.Name) && StringComparer.OrdinalIgnoreCase.Equals(x.Folder, y.Folder);

        public int GetHashCode((string Name, string Folder) obj) =>
            HashCode.Combine(ByName.GetHashCode(obj.Name), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Folder));
    }
}
