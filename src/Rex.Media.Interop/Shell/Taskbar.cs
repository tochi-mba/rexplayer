using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rex.Media.Interop.Shell;

/// <summary>One of the buttons under a window's taskbar thumbnail.</summary>
/// <param name="Id">The number the click reports.</param>
/// <param name="Icon">Its picture.</param>
/// <param name="Tooltip">What it says on hover (at most 259 characters).</param>
/// <param name="Visible">Whether it shows; a button cannot be taken away once added, only hidden.</param>
/// <param name="Enabled">Whether it can be clicked.</param>
public readonly record struct ThumbnailButton(int Id, TaskbarIcon? Icon, string Tooltip, bool Visible, bool Enabled);

/// <summary>The taskbar progress states this player uses (Windows' own values).</summary>
public enum TaskbarProgressKind
{
    None = 0,
    Indeterminate = 1,
    Normal = 2,
    Error = 4,
    Paused = 8,
}

/// <summary>
/// The taskbar's side of a window (ITaskbarList3): thumbnail buttons, the badge over the button and
/// its progress. Made and used on one thread. After Explorer restarts, or the window's taskbar
/// button is made again, Windows has forgotten all of it: make a new one and add the buttons again.
/// </summary>
public sealed class Taskbar : IDisposable
{
    /// <summary>The most buttons a thumbnail holds.</summary>
    public const int MaxButtons = 7;

    private ITaskbarList3? _list;

    private Taskbar(ITaskbarList3 list) => _list = list;

    /// <summary>The taskbar, or a <see cref="COMException"/> where there is none (no shell running).</summary>
    public static Taskbar Create()
    {
        var list = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(TaskbarList).GUID, throwOnError: true)!)!;
        try
        {
            list.HrInit();
            return new Taskbar(list);
        }
        catch
        {
            Marshal.ReleaseComObject(list);
            throw;
        }
    }

    /// <summary>The message Explorer sends a window when its taskbar button has been made (again).</summary>
    public static uint ButtonCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarButtonCreated");

    /// <summary>Adds <paramref name="buttons"/> under <paramref name="window"/>'s thumbnail: once per taskbar button.</summary>
    public unsafe void AddButtons(nint window, ReadOnlySpan<ThumbnailButton> buttons)
    {
        var native = Native(buttons);
        fixed (THUMBBUTTON* first = native)
        {
            List.ThumbBarAddButtons((HWND)window, (uint)native.Length, first);
        }
    }

    /// <summary>Changes the buttons already added (matched by id).</summary>
    public unsafe void UpdateButtons(nint window, ReadOnlySpan<ThumbnailButton> buttons)
    {
        var native = Native(buttons);
        fixed (THUMBBUTTON* first = native)
        {
            List.ThumbBarUpdateButtons((HWND)window, (uint)native.Length, first);
        }
    }

    /// <summary>Puts <paramref name="icon"/> over the window's taskbar button, described for screen readers; null takes it away.</summary>
    public void SetOverlay(nint window, TaskbarIcon? icon, string description) =>
        List.SetOverlayIcon((HWND)window, icon is null ? default : (HICON)icon.Handle, description);

    /// <summary>Shows <paramref name="percent"/> on the taskbar button in <paramref name="kind"/>'s colour, or nothing.</summary>
    public void SetProgress(nint window, TaskbarProgressKind kind, int percent)
    {
        List.SetProgressState((HWND)window, (TBPFLAG)kind);
        if (kind is TaskbarProgressKind.Normal or TaskbarProgressKind.Paused or TaskbarProgressKind.Error)
        {
            List.SetProgressValue((HWND)window, (ulong)Math.Clamp(percent, 0, 100), 100);
        }
    }

    public void Dispose()
    {
        if (_list is not null)
        {
            Marshal.ReleaseComObject(_list);
            _list = null;
        }
    }

    private ITaskbarList3 List => _list ?? throw new ObjectDisposedException(nameof(Taskbar));

    private static unsafe THUMBBUTTON[] Native(ReadOnlySpan<ThumbnailButton> buttons)
    {
        if (buttons.Length is 0 or > MaxButtons)
        {
            throw new ArgumentException($"A thumbnail holds one to {MaxButtons} buttons.", nameof(buttons));
        }

        var native = new THUMBBUTTON[buttons.Length];
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            native[i] = new THUMBBUTTON
            {
                dwMask = THUMBBUTTONMASK.THB_FLAGS | THUMBBUTTONMASK.THB_TOOLTIP | (button.Icon is null ? 0 : THUMBBUTTONMASK.THB_ICON),
                iId = (uint)button.Id,
                hIcon = button.Icon is null ? default : (HICON)button.Icon.Handle,
                dwFlags = (button.Visible ? 0 : THUMBBUTTONFLAGS.THBF_HIDDEN) | (button.Enabled ? 0 : THUMBBUTTONFLAGS.THBF_DISABLED),
            };
            var tip = button.Tooltip.AsSpan(0, Math.Min(button.Tooltip.Length, 259));
            var buffer = native[i].szTip.AsSpan();
            tip.CopyTo(buffer);
            buffer[tip.Length] = '\0';
        }

        return native;
    }
}
