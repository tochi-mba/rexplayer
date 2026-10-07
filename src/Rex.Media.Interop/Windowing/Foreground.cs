using System.Runtime.Versioning;
using Windows.Win32;

namespace Rex.Media.Interop.Windowing;

/// <summary>
/// Windows lets a process bring its window to the front only when the user is working with it. A
/// second launch the user just started may pass that right on, so the player it hands its files to
/// can come forward.
/// </summary>
[SupportedOSPlatform("windows5.0")]
public static class Foreground
{
    /// <summary>Lets any process take the foreground once (ASFW_ANY); false when Windows refuses.</summary>
    public static bool AllowAnyProcess() => PInvoke.AllowSetForegroundWindow(unchecked((uint)-1));
}
