using System.Runtime.InteropServices;

namespace Rex.Media.App.Tests;

/// <summary>
/// What a rexplayer window shows, read with PrintWindow from that window alone (never the screen),
/// as BGRA rows: so a test can see that a visualisation lights up and moves.
/// </summary>
internal static partial class WindowPicture
{
    private const uint RenderFullContent = 2;

    /// <summary>The virtual desktop in physical screen pixels, including attached monitors.</summary>
    public static System.Windows.Rect DesktopBounds
    {
        get
        {
            const int VirtualLeft = 76, VirtualTop = 77, VirtualWidth = 78, VirtualHeight = 79;
            var desktop = new System.Windows.Rect(GetSystemMetrics(VirtualLeft), GetSystemMetrics(VirtualTop),
                GetSystemMetrics(VirtualWidth), GetSystemMetrics(VirtualHeight));
            Assert.True(desktop.Width > 0 && desktop.Height > 0, "There is no visible Windows desktop.");
            return desktop;
        }
    }

    /// <summary>A desktop capture outside the virtual screen reads undefined black pixels.</summary>
    public static bool IsFullyOnDesktop(nint window)
    {
        var bounds = Bounds(window);
        var desktop = DesktopBounds;
        return bounds.Left >= desktop.Left && bounds.Top >= desktop.Top
            && bounds.Right <= desktop.Right && bounds.Bottom <= desktop.Bottom;
    }

    /// <summary>The window's picture, and its size.</summary>
    public static (int Width, int Height, byte[] Bgra) Take(nint window) => TakeCore(window, includeDesktopPopups: false);

    /// <summary>
    /// Captures the actual composed desktop region for a menu or ContentDialog. PrintWindow
    /// captures the host HWND without popup surfaces, which can silently yield an image of
    /// the underlying idle stage instead of the menu the UIA test just opened.
    /// Use only in the serial desktop UI tests, with a visible UIA popup witness.
    /// </summary>
    public static (int Width, int Height, byte[] Bgra) TakeDesktop(nint window) => TakeCore(window, includeDesktopPopups: true);

    private static (int Width, int Height, byte[] Bgra) TakeCore(nint window, bool includeDesktopPopups)
    {
        Assert.True(GetWindowRect(window, out var rect), "The window has no place on the screen.");
        var (width, height) = (rect.Right - rect.Left, rect.Bottom - rect.Top);
        Assert.True(width > 0 && height > 0, "The window has no captureable area.");
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (includeDesktopPopups)
            {
                Assert.True(BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020),
                    "The desktop, including its popups, could not be captured.");
            }
            else
            {
                Assert.True(PrintWindow(window, memory, RenderFullContent), "The window could not be drawn into a picture.");
            }
            var pixels = new byte[width * height * 4];
            var header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            Assert.True(GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, 0) == height, "The window's picture could not be read.");
            return (width, height, pixels);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    /// <summary>Where the window is on the screen, in pixels: the origin of its picture.</summary>
    public static System.Windows.Rect Bounds(nint window)
    {
        Assert.True(GetWindowRect(window, out var rect), "The window has no place on the screen.");
        return new System.Windows.Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>How bright a part of a picture is on average, 0 to 1.</summary>
    public static double Brightness((int Width, int Height, byte[] Bgra) picture, System.Windows.Rect area)
    {
        long sum = 0, count = 0;
        foreach (var i in Pixels(picture, area))
        {
            sum += picture.Bgra[i] + picture.Bgra[i + 1] + picture.Bgra[i + 2];
            count++;
        }

        return count == 0 ? 0 : sum / (count * 3 * 255.0);
    }

    /// <summary>How much a part of two pictures differs, on average, 0 to 1.</summary>
    public static double Change((int Width, int Height, byte[] Bgra) first, (int Width, int Height, byte[] Bgra) second, System.Windows.Rect area)
    {
        long sum = 0, count = 0;
        foreach (var i in Pixels(first, area))
        {
            if (i + 2 < second.Bgra.Length)
            {
                sum += Math.Abs(first.Bgra[i] - second.Bgra[i]) + Math.Abs(first.Bgra[i + 1] - second.Bgra[i + 1]) + Math.Abs(first.Bgra[i + 2] - second.Bgra[i + 2]);
                count++;
            }
        }

        return count == 0 ? 0 : sum / (count * 3 * 255.0);
    }

    private static IEnumerable<int> Pixels((int Width, int Height, byte[] Bgra) picture, System.Windows.Rect area)
    {
        for (var y = Math.Max(0, (int)area.Top); y < Math.Min(picture.Height, (int)area.Bottom); y += 2)
        {
            for (var x = Math.Max(0, (int)area.Left); x < Math.Min(picture.Width, (int)area.Right); x += 2)
            {
                yield return ((y * picture.Width) + x) * 4;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint thing);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint thing);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
}
