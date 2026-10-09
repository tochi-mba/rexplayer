using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;

namespace Rex.Media.Interop.Windowing;

/// <summary>
/// Shortcuts that reach a window even while another program is in front (UI-11): registered with
/// Windows by number, and reported by number when pressed. Made, used and disposed on the window's
/// own thread, where Windows delivers them.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const nuint SubclassId = 0x5245_5848;

    private readonly HWND _window;
    private readonly SUBCLASSPROC _procedure;
    private readonly HashSet<int> _registered = [];

    public GlobalHotkeys(nint window)
    {
        _window = (HWND)window;

        // Kept in a field so the delegate lives as long as Windows may call it.
        _procedure = WindowProcedure;
        PInvoke.SetWindowSubclass(_window, _procedure, SubclassId, 0);
    }

    /// <summary>Raised with the number of a shortcut pressed anywhere.</summary>
    public event EventHandler<int>? Pressed;

    /// <summary>Registers shortcut <paramref name="id"/>; false when another program already has those keys.</summary>
    public bool Register(int id, int virtualKey, bool ctrl, bool alt, bool shift)
    {
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT
            | (ctrl ? HOT_KEY_MODIFIERS.MOD_CONTROL : 0)
            | (alt ? HOT_KEY_MODIFIERS.MOD_ALT : 0)
            | (shift ? HOT_KEY_MODIFIERS.MOD_SHIFT : 0);
        if (!PInvoke.RegisterHotKey(_window, id, modifiers, (uint)virtualKey))
        {
            return false;
        }

        _registered.Add(id);
        return true;
    }

    /// <summary>Gives every registered shortcut back to Windows.</summary>
    public void Clear()
    {
        foreach (var id in _registered)
        {
            PInvoke.UnregisterHotKey(_window, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        Clear();
        PInvoke.RemoveWindowSubclass(_window, _procedure, SubclassId);
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (message == PInvoke.WM_HOTKEY && _registered.Contains((int)wParam.Value))
        {
            Pressed?.Invoke(this, (int)wParam.Value);
            return (LRESULT)0;
        }

        return PInvoke.DefSubclassProc(window, message, wParam, lParam);
    }
}
