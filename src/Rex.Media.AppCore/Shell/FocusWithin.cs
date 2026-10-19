namespace Rex.Media.AppCore.Shell;

/// <summary>
/// Whether focus is inside a part of the window: on the part itself or on anything it holds, such as
/// a button in the controls bar. The window gives the focused element and how to step to its parent.
/// </summary>
public static class FocusWithin
{
    public static bool IsInside<T>(T? focused, T part, Func<T, T?> parent)
        where T : class
    {
        for (var at = focused; at is not null; at = parent(at))
        {
            if (ReferenceEquals(at, part))
            {
                return true;
            }
        }

        return false;
    }
}
