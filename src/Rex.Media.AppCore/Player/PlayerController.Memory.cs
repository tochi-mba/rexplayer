using System.Globalization;
using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

public sealed partial class PlayerController
{
    // Where the restored queue's current item was, used by the first play.
    private TimeSpan? _restoredAt;

    /// <summary>What the player remembers between runs.</summary>
    public PlayerMemory Memory { get; }

    /// <summary>
    /// Where the current item was left last time, while the window offers to go back there (PB-11,
    /// "ask"); null when there is nothing to offer.
    /// </summary>
    public TimeSpan? ResumeOffer { get; private set; }

    /// <summary>The bookmarks of what is playing (PB-10), in order of position.</summary>
    public IReadOnlyList<Bookmark> Bookmarks => Item is { } item ? Memory.Bookmarks(MemoryKey(item)) : [];

    /// <summary>The media played most recently, newest first (LIB-07).</summary>
    public IReadOnlyList<string> Recent => Memory.Recent;

    /// <summary>Goes back to where the item was left, as offered; false when nothing is offered.</summary>
    public bool AcceptResume()
    {
        if (ResumeOffer is not { } at)
        {
            return false;
        }

        ResumeOffer = null;
        Seek(at);
        Say("Resumed at " + TimeText.Format(at, Duration));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Plays on from the start, and stops offering to go back.</summary>
    public void DeclineResume()
    {
        ResumeOffer = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Remembers where the current item is, so it can be resumed (the window calls this now and
    /// then, so a crash loses little); at either end it is forgotten instead.
    /// </summary>
    public void RememberPosition()
    {
        if (Item is { } item && _session is not null && State is not (Rex.Media.Engine.SessionState.Faulted or Rex.Media.Engine.SessionState.Opening) && Duration > TimeSpan.Zero)
        {
            Memory.Left(MemoryKey(item), Position, Duration);
        }
    }

    public void AddBookmark(string? name = null)
    {
        if (Item is not { } item)
        {
            Say("Open something to play first.");
            return;
        }

        var bookmarks = Bookmarks.ToList();
        var bookmark = new Bookmark(string.IsNullOrWhiteSpace(name) ? $"Bookmark {bookmarks.Count + 1}" : name.Trim(), Position);
        Memory.SetBookmarks(MemoryKey(item), [.. bookmarks, bookmark]);
        Say($"{bookmark.Name} at {TimeText.Format(bookmark.At, Duration)}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RenameBookmark(int index, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ChangeBookmarks(index, (bookmarks, at) => bookmarks[at] = bookmarks[at] with { Name = name.Trim() });
    }

    public void DeleteBookmark(int index) => ChangeBookmarks(index, (bookmarks, at) => bookmarks.RemoveAt(at));

    public void GoToBookmark(int index)
    {
        if (index >= 0 && index < Bookmarks.Count)
        {
            var bookmark = Bookmarks[index];
            Seek(bookmark.At);
            Say(bookmark.Name);
        }
    }

    /// <summary>Remembers the current item and position in quick slot <paramref name="number"/> (LIB-08).</summary>
    public void SetSlot(int number)
    {
        if (Item is not { } item)
        {
            Say("Open something to play first.");
            return;
        }

        Memory.SetSlot(number, new QuickSlot(item.Location, item.Start + Position));
        Say($"Quick slot {number}: {Title}");
    }

    /// <summary>Plays what quick slot <paramref name="number"/> remembers, from where it was.</summary>
    public void PlaySlot(int number)
    {
        if (Memory.Slot(number) is not { } slot)
        {
            Say($"Quick slot {number} is empty. Ctrl+Shift+{number} keeps what is playing there.");
            return;
        }

        var index = Playlist.Items.ToList().FindIndex(item => item.Location == slot.Location && slot.At >= item.Start && (item.End is null || slot.At < item.End));
        if (index < 0)
        {
            Playlist.Add([new PlaylistItem(slot.Location)]);
            index = Playlist.Items.Count - 1;
        }

        var target = Playlist.JumpTo(index);
        Start(target, slot.At - target.Start);
    }

    /// <summary>Forgets every resume point and the recent media (PRIV-03).</summary>
    public void ClearHistory()
    {
        Memory.ClearHistory();
        Library?.ClearPlays();
        Say("History cleared");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the queue, the item playing and where, to restore at the next start (LIB-03).</summary>
    public void SaveQueue()
    {
        RememberPosition();
        Memory.Queue = Settings.RestoreQueue && Playlist.Items.Count > 0
            ? new QueueSnapshot([.. Playlist.Items.Select(Kept)], Playlist.CurrentIndex, Position)
            : null;
    }

    /// <summary>Puts back the queue kept at the last close, ready to play from where it was; false when there is none.</summary>
    public bool RestoreQueue()
    {
        if (!Settings.RestoreQueue || Memory.Queue is not { } queue)
        {
            return false;
        }

        Playlist.Clear();
        Playlist.Add(queue.Items.Select(Restored));
        if (queue.Current >= 0 && queue.Current < Playlist.Items.Count)
        {
            Playlist.JumpTo(queue.Current);
            _restoredAt = queue.At;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Records the exact queue, position and playback state just before an app-initiated upgrade.
    /// This is deliberate one-time recovery, independent of the normal RestoreQueue preference.
    /// </summary>
    public void SaveForUpdate(
        string librarySource, bool libraryVisible, string search, string? groupName,
        string? groupDetail, string? seasonName, bool playlistVisible)
    {
        RememberPosition();
        var queue = Playlist.Items.Count == 0 ? null
            : new QueueSnapshot([.. Playlist.Items.Select(Kept)], Playlist.CurrentIndex, Position);
        var active = Item is not null && State is not (Rex.Media.Engine.SessionState.Idle
            or Rex.Media.Engine.SessionState.Ended or Rex.Media.Engine.SessionState.Faulted);
        Memory.Updating = new UpdateSession(queue, active, IsPlaying, librarySource, libraryVisible,
            search, groupName, groupDetail, seasonName, playlistVisible, Speed, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Consumes a pending upgrade hand-off once on the new app's ordinary startup. Invalid, stale
    /// or empty hand-offs fall back to normal startup. A paused video remains paused; a playing
    /// video starts at its saved position, without prompting for a second resume.
    /// </summary>
    public UpdateSession? RestoreAfterUpdate()
    {
        var saved = Memory.Updating;
        if (saved is null)
        {
            return null;
        }

        // A failed or abandoned upgrade must not unexpectedly resume months later.
        if (saved.SavedAt > DateTimeOffset.UtcNow.AddMinutes(5)
            || saved.SavedAt < DateTimeOffset.UtcNow.AddDays(-7))
        {
            Memory.Updating = null;
            return null;
        }

        if (saved.Queue is { Items: { Count: > 0 } items } queue)
        {
            Playlist.Clear();
            Playlist.Add(items.Select(Restored));
            if (queue.Current >= 0 && queue.Current < Playlist.Items.Count)
            {
                Playlist.JumpTo(queue.Current);
                _restoredAt = queue.At >= TimeSpan.Zero ? queue.At : TimeSpan.Zero;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            if (saved.Active && Playlist.Current is { } current)
            {
                SetSpeed(saved.Speed);
                Start(current, paused: !saved.Playing);
            }
        }

        Memory.Updating = null;
        return saved;
    }

    /// <summary>The quick slot and bookmark commands; false for any other.</summary>
    private bool ExecuteMemory(string commandId)
    {
        if (CommandCatalog.SlotNumber(commandId) is { } slot)
        {
            if (commandId.StartsWith(CommandCatalog.SetSlotPrefix, StringComparison.Ordinal))
            {
                SetSlot(slot);
            }
            else
            {
                PlaySlot(slot);
            }

            return true;
        }

        switch (commandId)
        {
            case CommandCatalog.AddBookmark:
                AddBookmark();
                return true;
            case CommandCatalog.ClearHistory:
                ClearHistory();
                return true;
            case CommandCatalog.Resume:
                if (!AcceptResume())
                {
                    Say("There is nowhere to go back to.");
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The start of an item: what was playing before is remembered, and where this one was left
    /// is offered, or gone back to, as the settings say.
    /// </summary>
    private TimeSpan BeforeStart(PlaylistItem item, TimeSpan startAt, int? audioTrack)
    {
        RememberPosition();
        ResumeOffer = null;
        if (_restoredAt is { } restored && Playlist.Current == item && startAt == TimeSpan.Zero)
        {
            _restoredAt = null;
            return restored;
        }

        _restoredAt = null;
        if (startAt > TimeSpan.Zero || audioTrack is not null || Settings.ResumePlayback == ResumeChoice.Never || Memory.ResumePoint(MemoryKey(item)) is not { } at)
        {
            return startAt;
        }

        if (Settings.ResumePlayback == ResumeChoice.Always)
        {
            Say("Resuming at " + TimeText.Format(at, TimeSpan.Zero));
            return at;
        }

        ResumeOffer = at;
        return startAt;
    }

    /// <summary>A file, or a part of one (a cue sheet's track), as the memory knows it.</summary>
    private static string MemoryKey(PlaylistItem item) =>
        item.IsPart ? item.Location + "#" + item.Start.Ticks.ToString(CultureInfo.InvariantCulture) : item.Location;

    private void ChangeBookmarks(int index, Action<List<Bookmark>, int> change)
    {
        var bookmarks = Bookmarks.ToList();
        if (Item is { } item && index >= 0 && index < bookmarks.Count)
        {
            change(bookmarks, index);
            Memory.SetBookmarks(MemoryKey(item), bookmarks);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
