using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Rex.Media.App.Tests;

/// <summary>
/// Real keyboard and mouse input, for the few things UI Automation cannot reach: shortcuts typed
/// on the keyboard, the picture's right-click menu, and global shortcuts that work while another
/// program is in front. Everything else is driven through UI Automation, which needs no focus.
/// </summary>
internal static partial class Input
{
    private const uint KeyboardInput = 1;
    private const uint MouseInput = 0;
    private const uint KeyUp = 0x0002;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;

    /// <summary>Brings the app's window to the front with the keyboard focus, waiting until it is.</summary>
    public static void Focus(AppProcess app)
    {
        Wait.For(() =>
        {
            if (GetForegroundWindow() == app.Handle)
            {
                return true;
            }

            SetForegroundWindow(app.Handle);
            try
            {
                app.Window.SetFocus();
            }
            catch (InvalidOperationException)
            {
                // Windows may refuse once; the wait asks again.
            }

            return GetForegroundWindow() == app.Handle;
        }, "the window to come to the front");
    }

    /// <summary>Maximises the window as its title bar's button does.</summary>
    public static void Maximize(AppProcess app) => ShowWindow(app.Handle, 3);

    /// <summary>Restores a maximised window to its own size.</summary>
    public static void Restore(AppProcess app) => ShowWindow(app.Handle, 9);

    /// <summary>Presses and releases a shortcut written as rexplayer writes them ("Ctrl+Alt+F9", "M").</summary>
    public static void Press(string chord)
    {
        var parsed = Rex.Media.AppCore.Commands.KeyChord.Parse(chord);
        var keys = new List<ushort>();
        if (parsed.Modifiers.HasFlag(Rex.Media.AppCore.Commands.KeyModifiers.Ctrl))
        {
            keys.Add(0x11);
        }

        if (parsed.Modifiers.HasFlag(Rex.Media.AppCore.Commands.KeyModifiers.Alt))
        {
            keys.Add(0x12);
        }

        if (parsed.Modifiers.HasFlag(Rex.Media.AppCore.Commands.KeyModifiers.Shift))
        {
            keys.Add(0x10);
        }

        keys.Add((ushort)(Rex.Media.AppCore.Commands.VirtualKeys.Code(parsed.Key) ?? throw new ArgumentException(chord)));
        var inputs = keys.Select(key => Key(key, 0)).Concat(Enumerable.Reverse(keys).Select(key => Key(key, KeyUp))).ToArray();
        Assert.Equal((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<InputRecord>()));
    }

    /// <summary>Right-clicks the middle of a control, as a person opening its menu does.</summary>
    public static void RightClick(AutomationElement element)
    {
        var bounds = element.Current.BoundingRectangle;
        Assert.True(SetCursorPos((int)(bounds.Left + (bounds.Width / 2)), (int)(bounds.Top + (bounds.Height / 2))), "The pointer could not be moved.");
        var inputs = new[] { Mouse(RightDown), Mouse(RightUp) };
        Assert.Equal(2u, SendInput(2, inputs, Marshal.SizeOf<InputRecord>()));
    }

    private static InputRecord Key(ushort key, uint flags) =>
        new() { Type = KeyboardInput, Data = new InputUnion { Keyboard = new KeyboardRecord { VirtualKey = key, Flags = flags } } };

    private static InputRecord Mouse(uint flags) =>
        new() { Type = MouseInput, Data = new InputUnion { Mouse = new MouseRecord { Flags = flags } } };

    [StructLayout(LayoutKind.Sequential)]
    private struct InputRecord
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseRecord Mouse;

        [FieldOffset(0)]
        public KeyboardRecord Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseRecord
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardRecord
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] InputRecord[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
}
