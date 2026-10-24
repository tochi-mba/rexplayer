using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Rex.Media.App.Tests;

/// <summary>
/// Real clicks and key presses for the few things UI Automation has no pattern for: a single or
/// double click on a library line, a right-click that opens its menu, and keys typed into the
/// focused control. The window is brought forward first, so the input reaches rexplayer alone.
/// </summary>
internal static partial class Pointer
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint KeyUp = 0x0002;

    public const ushort Escape = 0x1B;
    public const ushort Enter = 0x0D;
    public const ushort Down = 0x28;

    public static void Click(AppProcess app, AutomationElement element) => Mouse(app, element, LeftDown, LeftUp, 1);

    public static void DoubleClick(AppProcess app, AutomationElement element) => Mouse(app, element, LeftDown, LeftUp, 2);

    public static void RightClick(AppProcess app, AutomationElement element) => Mouse(app, element, RightDown, RightUp, 1);

    /// <summary>Move the real pointer over a fraction of a visible slider without clicking or seeking.</summary>
    public static void Hover(AppProcess app, AutomationElement element, double fraction)
    {
        Forward(app);
        var box = element.Current.BoundingRectangle;
        Assert.False(box.IsEmpty, "The hover target has no place on screen.");
        Assert.InRange(fraction, 0, 1);
        // A single instantaneous cursor warp can be coalesced with a WinUI
        // Slider's own pointer-enter processing. Enter from outside, allow the
        // event to be delivered, then approach the requested position as a
        // physical pointer would. This does not click or alter playback.
        var y = (int)(box.Top + (box.Height * 0.5));
        Assert.True(SetCursorPos((int)box.Left - 12, y),
            "The pointer could not approach the target.");
        Thread.Sleep(80);
        Assert.True(SetCursorPos((int)(box.Left + (box.Width * 0.15)), y),
            "The pointer could not enter the slider.");
        Thread.Sleep(80);
        Assert.True(SetCursorPos((int)(box.Left + (box.Width * fraction)), y),
            "The pointer could not hover over the target.");
    }

    /// <summary>Gives <paramref name="element"/> the keyboard focus and presses <paramref name="key"/> once.</summary>
    public static void Key(AppProcess app, AutomationElement element, ushort key)
    {
        Forward(app, element);
        Send([Keyboard(key, 0), Keyboard(key, KeyUp)]);
    }

    /// <summary>
    /// Focuses a control by id before typing. WinUI can replace a control while its view is
    /// refreshed, so a stale UI Automation element is looked up again instead of failing a test.
    /// </summary>
    public static void Key(AppProcess app, string automationId, ushort key)
    {
        Wait.For(() =>
        {
            try
            {
                Forward(app, app.Find(automationId));
                return true;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }, automationId + " to accept keyboard focus");
        Key(key);
    }

    /// <summary>Presses <paramref name="key"/> once wherever the keyboard focus already is.</summary>
    public static void Key(ushort key)
    {
        // The focused element may be inside a separate menu popup window. Do not
        // bring the owner window forward: Escape must reach the active popup.
        Send([Keyboard(key, 0), Keyboard(key, KeyUp)]);
    }

    private static void Mouse(AppProcess app, AutomationElement element, uint down, uint up, int clicks)
    {
        Forward(app);
        // A list line's own clickable point is often refused; the middle of its box is on the line.
        var box = element.Current.BoundingRectangle;
        Assert.False(box.IsEmpty, "The line has no place on the screen.");
        Assert.True(SetCursorPos((int)(box.Left + (box.Width / 2)), (int)(box.Top + (box.Height / 2))), "The pointer could not be moved over the line.");
        var inputs = new List<Input>();
        for (var i = 0; i < clicks; i++)
        {
            inputs.Add(new Input { Type = InputMouse, Mouse = new MouseInput { Flags = down } });
            inputs.Add(new Input { Type = InputMouse, Mouse = new MouseInput { Flags = up } });
        }

        Send([.. inputs]);
    }

    /// <summary>
    /// Brings the window that holds <paramref name="element"/> to the front, and gives the element
    /// the focus. A menu is its own window: forcing the main window forward would leave the menu
    /// open yet take the keys away from it. Without an element, the focus is left where it is.
    /// </summary>
    private static void Forward(AppProcess app, AutomationElement? element = null)
    {
        if (element is null)
        {
            // The selected list line already has keyboard focus. Never move it back to the
            // window root merely to bring the app forward before pressing Enter.
            if (GetForegroundWindow() != app.Handle)
            {
                _ = SetForegroundWindow(app.Handle);
                Wait.For(() => GetForegroundWindow() == app.Handle, "rexplayer to be the window in front");
            }

            return;
        }

        // A flyout has its own native window. UI Automation can focus its item even when
        // Windows refuses to mark that popup handle as the foreground top-level window.
        var window = WindowOf(element) is not 0 and var own ? own : app.Handle;
        element.SetFocus();
        if (GetForegroundWindow() != window)
        {
            _ = SetForegroundWindow(window);
        }

        element.SetFocus();
    }

    /// <summary>The handle of the nearest window around <paramref name="element"/>, or 0.</summary>
    private static nint WindowOf(AutomationElement? element)
    {
        for (var current = element; current is not null; current = TreeWalker.ControlViewWalker.GetParent(current))
        {
            if (current.Current.NativeWindowHandle is not 0 and var handle)
            {
                return handle;
            }
        }

        return 0;
    }

    private static Input Keyboard(ushort key, uint flags) => new() { Type = InputKeyboard, Keyboard = new KeyboardInput { Key = key, Flags = flags } };

    private static void Send(Input[] inputs) =>
        Assert.Equal((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public MouseInput Mouse;

        [FieldOffset(8)]
        public KeyboardInput Keyboard;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
