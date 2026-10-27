using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Rex.PerfTour;

/// <summary>
/// A real double click for the one thing UI Automation has no pattern for: opening a library line.
/// The window is brought forward first, so the input reaches the player alone, and the pointer is
/// put back where the person left it afterwards.
/// </summary>
internal static partial class Mouse
{
    private const uint InputMouse = 0;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint MouseMove = 0x0001;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesktop = 0x4000;
    private const int VirtualDesktopLeft = 76;
    private const int VirtualDesktopTop = 77;
    private const int VirtualDesktopWidth = 78;
    private const int VirtualDesktopHeight = 79;

    public static void DoubleClick(AttachedApp app, AutomationElement element)
    {
        _ = SetForegroundWindow(app.Process.MainWindowHandle);
        var where = element.GetClickablePoint();
        var restore = GetCursorPosition();
        Send(Move(where.X, where.Y));
        Send(Button(LeftDown), Button(LeftUp), Button(LeftDown), Button(LeftUp));
        Send(Move(restore.X, restore.Y));
    }

    private static (double X, double Y) GetCursorPosition()
    {
        _ = GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    private static Input Move(double x, double y)
    {
        var left = GetSystemMetrics(VirtualDesktopLeft);
        var top = GetSystemMetrics(VirtualDesktopTop);
        var width = Math.Max(1, GetSystemMetrics(VirtualDesktopWidth));
        var height = Math.Max(1, GetSystemMetrics(VirtualDesktopHeight));
        return new Input
        {
            Type = InputMouse,
            Data = new MouseInput
            {
                Dx = (int)Math.Round((x - left) * 65535 / width),
                Dy = (int)Math.Round((y - top) * 65535 / height),
                Flags = MouseMove | MouseAbsolute | MouseVirtualDesktop,
            },
        };
    }

    private static Input Button(uint flag) => new() { Type = InputMouse, Data = new MouseInput { Flags = flag } };

    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
        {
            throw new TourStepException("The click was not delivered.");
        }

        Thread.Sleep(30);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
