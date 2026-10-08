using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Player;

/// <summary>A named position in a file (PB-10).</summary>
public sealed record Bookmark(string Name, TimeSpan At);

/// <summary>A remembered place to play from: a quick slot (LIB-08).</summary>
public sealed record QuickSlot(string Location, TimeSpan At);

/// <summary>An item of a saved queue, with the part of its file it plays.</summary>
public sealed record QueuedItem(string Location, string? Title, string? Artist, TimeSpan Start, TimeSpan? End);

/// <summary>The queue as it was when the player closed (LIB-03): its items, the one playing, and where.</summary>
public sealed record QueueSnapshot(IReadOnlyList<QueuedItem> Items, int Current, TimeSpan At);

/// <summary>
/// What the player remembers between runs, kept in a <see cref="RexStore"/>: where each file was
/// left (PB-11), bookmarks (PB-10), recent media (LIB-07), quick slots (LIB-08) and the queue
/// (LIB-03). Resume points and recent media are history, which the user can turn off and clear
/// (PRIV-03); bookmarks and slots are the user's own and stay. Files are known by a hash of their
/// location, so the store does not list what was watched.
/// </summary>
public sealed class PlayerMemory(RexStore store)
{
    /// <summary>How many recent items are kept.</summary>
    public const int RecentLimit = 20;

    /// <summary>The quick slots, numbered from 1.</summary>
    public const int Slots = 9;

    /// <summary>Closer than this to either end, a file is not worth resuming.</summary>
    public static readonly TimeSpan ResumeMargin = TimeSpan.FromSeconds(10);

    private const string ResumePrefix = "resume/";
    private const string BookmarkPrefix = "bookmarks/";
    private const string RecentKey = "recent";
    private const string QueueKey = "queue";

    public RexStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Whether resume points and recent media are kept; when off, nothing new is.</summary>
    public bool KeepsHistory { get; set; } = true;

    /// <summary>The key a file is kept under: a hash of its location, the same however a Windows path is cased.</summary>
    public static string KeyFor(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var text = location.Contains("://", StringComparison.Ordinal) ? location : location.ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>Where <paramref name="location"/> was left, if it is worth going back to.</summary>
    public TimeSpan? ResumePoint(string location) =>
        Store.Get(ResumePrefix + KeyFor(location)) is { } text && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ? TimeSpan.FromTicks(ticks) : null;

    /// <summary>
    /// Remembers that <paramref name="location"/> was left at <paramref name="position"/> of
    /// <paramref name="duration"/> (zero when unknown); near either end, forgets it instead.
    /// </summary>
    public void Left(string location, TimeSpan position, TimeSpan duration)
    {
        var key = ResumePrefix + KeyFor(location);
        var worth = position >= ResumeMargin && (duration <= TimeSpan.Zero || duration - position >= ResumeMargin);
        if (!worth)
        {
            Store.Remove(key);
        }
        else if (KeepsHistory)
        {
            Store.Set(key, position.Ticks.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The media played most recently, newest first.</summary>
    public IReadOnlyList<string> Recent => Read(RecentKey, MemoryJson.Default.ListString) ?? [];

    /// <summary>Puts <paramref name="location"/> at the top of the recent media.</summary>
    public void Played(string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (KeepsHistory)
        {
            Write(RecentKey, [location, .. Recent.Where(old => old != location).Take(RecentLimit - 1)], MemoryJson.Default.ListString);
        }
    }

    /// <summary>Forgets every resume point and the recent media (PRIV-03).</summary>
    public void ClearHistory()
    {
        Store.RemovePrefix(ResumePrefix);
        Store.Remove(RecentKey);
    }

    public IReadOnlyList<Bookmark> Bookmarks(string location) => Read(BookmarkPrefix + KeyFor(location), MemoryJson.Default.ListBookmark) ?? [];

    /// <summary>Keeps <paramref name="bookmarks"/> for <paramref name="location"/>, in order of position; none forgets them.</summary>
    public void SetBookmarks(string location, IEnumerable<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        var key = BookmarkPrefix + KeyFor(location);
        var sorted = bookmarks.OrderBy(bookmark => bookmark.At).ToList();
        if (sorted.Count == 0)
        {
            Store.Remove(key);
            return;
        }

        Write(key, sorted, MemoryJson.Default.ListBookmark);
    }

    public QuickSlot? Slot(int number) => Read(SlotKey(number), MemoryJson.Default.QuickSlot);

    public void SetSlot(int number, QuickSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        Write(SlotKey(number), slot, MemoryJson.Default.QuickSlot);
    }

    /// <summary>The queue kept when the player last closed, or null.</summary>
    public QueueSnapshot? Queue
    {
        get => Read(QueueKey, MemoryJson.Default.QueueSnapshot);
        set
        {
            if (value is null || value.Items.Count == 0)
            {
                Store.Remove(QueueKey);
                return;
            }

            Write(QueueKey, value, MemoryJson.Default.QueueSnapshot);
        }
    }

    private static string SlotKey(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, Slots);
        return "slot/" + number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A kept value, or null when there is none or it no longer reads (from an older version, say).</summary>
    private T? Read<T>(string key, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (Store.Get(key) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Write<T>(string key, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) => Store.Set(key, JsonSerializer.Serialize(value, type));
}

[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "ListString")]
[JsonSerializable(typeof(List<Bookmark>), TypeInfoPropertyName = "ListBookmark")]
[JsonSerializable(typeof(QuickSlot))]
[JsonSerializable(typeof(QueueSnapshot))]
internal sealed partial class MemoryJson : JsonSerializerContext;
