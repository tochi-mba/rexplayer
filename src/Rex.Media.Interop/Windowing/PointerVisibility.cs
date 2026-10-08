using System.Runtime.Versioning;
using Windows.Win32;

namespace Rex.Media.Interop.Windowing;

/// <summary>
/// Hides the mouse pointer over the app's windows and shows it again. Windows counts hides and
/// shows, so this keeps them balanced: hiding twice and showing once still shows it.
/// </summary>
[SupportedOSPlatform("windows5.0")]
public static class PointerVisibility
{
    private static bool _hidden;

    /// <summary>Hides (<paramref name="hidden"/>) or shows the pointer; call on the window's thread.</summary>
    public static void SetHidden(bool hidden)
    {
        if (hidden == _hidden)
        {
            return;
        }

        _hidden = hidden;

        // The display count it returns says nothing this needs: the hides and shows here stay paired.
        _ = PInvoke.ShowCursor(!hidden);
    }
}
