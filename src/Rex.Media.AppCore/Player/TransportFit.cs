namespace Rex.Media.AppCore.Player;

/// <summary>
/// The room on the transport's line of buttons, in the window's units. Every width counts the space
/// after it, so the parts add up.
/// </summary>
/// <param name="Width">The width of the line.</param>
/// <param name="Gap">The space between the buttons, the title and the speed and volume group.</param>
/// <param name="Fixed">The buttons that always stay on the line: Previous, Play and Next.</param>
/// <param name="Movable">The buttons that can move into More, in the order they go.</param>
/// <param name="More">The More button that holds them.</param>
/// <param name="Group">The speed and volume group, as wide as it is with everything in it.</param>
/// <param name="Speed">The speed button, part of <paramref name="Group"/>: on a short window it can go into More too.</param>
/// <param name="Short">Whether the window is short, so a single line of buttons is worth more than seeing every button.</param>
public sealed record TransportRoom(double Width, double Gap, double Fixed, IReadOnlyList<double> Movable, double More, double Group, double Speed, bool Short);

/// <summary>What the transport's line of buttons holds.</summary>
/// <param name="Moved">How many of the movable buttons, counted in the order they go, are in More.</param>
/// <param name="SpeedMoved">Whether the speed button is in More.</param>
/// <param name="Wrapped">Whether the speed and volume group has a line of its own under the buttons.</param>
public readonly record struct TransportLayout(int Moved, bool SpeedMoved, bool Wrapped)
{
    /// <summary>Whether More shows: anything moved into it.</summary>
    public bool MoreShown => Moved > 0 || SpeedMoved;
}

/// <summary>
/// Fits the transport into its width (UI-10) so that no button is ever cut off or scrolled away.
/// The buttons claim their width first and the title takes what is left. When everything does not fit
/// on one line with room for a readable title, the speed and volume group takes a line of its own;
/// when the buttons alone still do not fit, the secondary ones move, one at a time and in their order,
/// into a More menu. A short window keeps a single line instead, moving buttons (and at last the speed
/// button) into More before it gives the group a line of its own, since every line the transport takes
/// is taken from the picture.
/// </summary>
public static class TransportFit
{
    /// <summary>The least width worth giving the title before the group takes a line of its own.</summary>
    public const double LeastTitle = 96;

    /// <summary>Below this height a window is short: the transport keeps to one line of buttons.</summary>
    public const double ShortHeight = 480;

    /// <summary>Whether a window whose content is <paramref name="height"/> tall is short.</summary>
    public static bool IsShort(double height) => height > 0 && height < ShortHeight;

    /// <summary>What the line holds in <paramref name="room"/>.</summary>
    public static TransportLayout Decide(TransportRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var title = room.Short ? 0 : LeastTitle;
        var count = room.Movable.Count;
        if (OneLine(room, 0, false, title) <= room.Width)
        {
            return new TransportLayout(0, false, false);
        }

        if (room.Short)
        {
            for (var moved = 1; moved <= count; moved++)
            {
                if (OneLine(room, moved, false, title) <= room.Width)
                {
                    return new TransportLayout(moved, false, false);
                }
            }

            if (OneLine(room, count, true, title) <= room.Width)
            {
                return new TransportLayout(count, true, false);
            }
        }

        for (var moved = 0; moved < count; moved++)
        {
            if (Buttons(room, moved, false) + room.Gap + title + room.Gap <= room.Width)
            {
                return new TransportLayout(moved, false, true);
            }
        }

        // Nothing narrower is possible: Previous, Play, Next and More, with the group under them.
        return new TransportLayout(count, false, true);
    }

    /// <summary>The buttons: the fixed ones, the movable ones not moved, and More when anything moved.</summary>
    private static double Buttons(TransportRoom room, int moved, bool speedMoved)
    {
        var width = room.Fixed;
        for (var i = moved; i < room.Movable.Count; i++)
        {
            width += room.Movable[i];
        }

        return moved > 0 || speedMoved ? width + room.More : width;
    }

    /// <summary>The buttons, the title and the group on one line.</summary>
    private static double OneLine(TransportRoom room, int moved, bool speedMoved, double title) =>
        Buttons(room, moved, speedMoved) + room.Gap + title + room.Gap + room.Group - (speedMoved ? room.Speed : 0);
}
