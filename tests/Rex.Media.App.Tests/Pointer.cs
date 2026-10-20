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

    public static void Click(AppProcess app, AutomationElement element) => Mouse(app, element, LeftDown, LeftUp, 1);

    public static void DoubleClick(AppProcess app, AutomationElement element) => Mouse(app, element, LeftDown, LeftUp, 2);

    public static void RightClick(AppProcess app, AutomationElement element) => Mouse(app, element, RightDown, RightUp, 1);

    /// <summary>Gives <paramref name="element"/> the keyboard focus and presses <paramref name="key"/> once.</summary>
    public static void Key(AppProcess app, AutomationElement element, ushort key)
    {
        Forward(app);
        element.SetFocus();
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

    private static void Forward(AppProcess app)
    {
        // UI Automation's focus may move the foreground where SetForegroundWindow alone is refused.
        app.Window.SetFocus();
        _ = SetForegroundWindow(app.Handle);
        Wait.For(() => GetForegroundWindow() == app.Handle, "rexplayer to be the window in front");
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
