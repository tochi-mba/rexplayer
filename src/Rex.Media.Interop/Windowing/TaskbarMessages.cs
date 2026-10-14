using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rex.Media.Interop.Windowing;

/// <summary>
/// What the taskbar says to a window, heard by subclassing it (a WinUI window's procedure is its
/// own): a thumbnail button clicked, the taskbar button made again (Explorer restarted, or the
/// window hidden and shown), and changes of scale or theme that call for new icons. The procedure
/// only raises events and passes everything on; it never blocks. Made, used and disposed on the
/// window's thread.
/// </summary>
public sealed class TaskbarMessages : IDisposable
{
    private const nuint SubclassId = 0x5245_5854;
    private const int ThumbnailClicked = 0x1800;

    private readonly HWND _window;
    private readonly SUBCLASSPROC _procedure;
    private readonly uint _buttonCreated;

    public TaskbarMessages(nint window)
    {
        _window = (HWND)window;
        _buttonCreated = Shell.Taskbar.ButtonCreatedMessage;

        // A player run as administrator still hears Explorer, which runs as the user.
        PInvoke.ChangeWindowMessageFilterEx(_window, _buttonCreated, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW);
        PInvoke.ChangeWindowMessageFilterEx(_window, PInvoke.WM_COMMAND, WINDOW_MESSAGE_FILTER_ACTION.MSGFLT_ALLOW);

        // Kept in a field so the delegate lives as long as Windows may call it.
        _procedure = WindowProcedure;
        PInvoke.SetWindowSubclass(_window, _procedure, SubclassId, 0);
    }

    /// <summary>Raised with a thumbnail button's id when it is clicked.</summary>
    public event EventHandler<int>? ButtonClicked;

    /// <summary>Raised when the window's taskbar button has been made (again): its thumbnail buttons must be added anew.</summary>
    public event EventHandler? ButtonCreated;

    /// <summary>Raised when the scale or the theme changed, so the icons should be drawn again.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>The small-icon size at the window's scale, which the thumbnail buttons and the badge use.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.14393")]
    public static int IconSize(nint window)
    {
        var dpi = PInvoke.GetDpiForWindow((HWND)window);
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi == 0 ? 96 : dpi);
        return size > 0 ? size : 16;
    }

    /// <summary>Whether the taskbar is light (Windows' mode for the taskbar and Start), read from the user's settings.</summary>
    public static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    public void Dispose() => PInvoke.RemoveWindowSubclass(_window, _procedure, SubclassId);

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (message == PInvoke.WM_COMMAND && ((wParam.Value >> 16) & 0xFFFF) == ThumbnailClicked)
        {
            ButtonClicked?.Invoke(this, (int)(wParam.Value & 0xFFFF));
            return (LRESULT)0;
        }

        if (message == _buttonCreated)
        {
            ButtonCreated?.Invoke(this, EventArgs.Empty);
        }
        else if (message is PInvoke.WM_DPICHANGED or PInvoke.WM_THEMECHANGED or PInvoke.WM_SETTINGCHANGE)
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }

        return PInvoke.DefSubclassProc(window, message, wParam, lParam);
    }
}
