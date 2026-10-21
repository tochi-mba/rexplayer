using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rex.Media.Library;

namespace Rex.Media.AppCore.Library;

/// <summary>
/// What a person keeps for each book, in the library's store: the speed they listen to it at and
/// whether they marked it finished. Where a book is up to is not kept here: it is worked out from the
/// parts' plays and resume points (<see cref="ListeningViews.PlaceIn"/>). Books are known by a hash of
/// <see cref="ListeningViews.BookKey"/>, so the store does not list what was read.
/// </summary>
public sealed class AudiobookMemory(RexStore store)
{
    private const string SpeedPrefix = "audiobook-speed/";
    private const string FinishedPrefix = "audiobook-finished/";

    public RexStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The speed the book is listened to at; null when it was never changed from normal.</summary>
    public double? Speed(LibraryGroup book) =>
        Store.Get(SpeedPrefix + KeyFor(book)) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : null;

    /// <summary>Keeps <paramref name="speed"/> for the book; normal speed forgets it.</summary>
    public void KeepSpeed(LibraryGroup book, double speed)
    {
        var key = SpeedPrefix + KeyFor(book);
        if (Math.Abs(speed - 1) < 0.005)
        {
            Store.Remove(key);
        }
        else
        {
            Store.Set(key, speed.ToString("0.##", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Whether the person marked the book finished.</summary>
    public bool Finished(LibraryGroup book) => Store.Get(FinishedPrefix + KeyFor(book)) is not null;

    /// <summary>Marks the book finished, or not.</summary>
    public void SetFinished(LibraryGroup book, bool finished)
    {
        var key = FinishedPrefix + KeyFor(book);
        if (finished)
        {
            Store.Set(key, "1");
        }
        else
        {
            Store.Remove(key);
        }
    }

    private static string KeyFor(LibraryGroup book) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ListeningViews.BookKey(book))))[..32];
}
