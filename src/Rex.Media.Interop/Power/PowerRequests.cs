using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace Rex.Media.Interop.Power;

/// <summary>
/// Keeps the PC (and, for pictures, the display) awake while media plays, through the calling
/// thread's execution state. Call it from one long-lived thread, the window's, since the request
/// belongs to the thread that made it.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public static class PowerRequests
{
    /// <summary>
    /// <paramref name="display"/> keeps the screen on (video); <paramref name="system"/> alone keeps
    /// the PC from sleeping but lets the screen saver start (music). Both false releases the request.
    /// </summary>
    public static void Hold(bool display, bool system)
    {
        var state = EXECUTION_STATE.ES_CONTINUOUS;
        if (display)
        {
            state |= EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED;
        }
        else if (system)
        {
            state |= EXECUTION_STATE.ES_SYSTEM_REQUIRED;
        }

        PInvoke.SetThreadExecutionState(state);
    }
}
