using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rex.Media.Interop.Shell;

/// <summary>An icon made from pixels, for the taskbar's thumbnail buttons and its badge; destroyed when disposed.</summary>
public sealed unsafe class TaskbarIcon : IDisposable
{
    private HICON _icon;

    private TaskbarIcon(HICON icon, int size)
    {
        _icon = icon;
        Size = size;
    }

    public int Size { get; }

    public nint Handle => _icon;

    /// <summary>
    /// An icon <paramref name="size"/> pixels a side from <paramref name="pixels"/>: rows top to bottom,
    /// blue, green, red and alpha bytes, alpha not multiplied in.
    /// </summary>
    public static TaskbarIcon FromPixels(ReadOnlySpan<byte> pixels, int size)
    {
        if (size <= 0 || pixels.Length != size * size * 4)
        {
            throw new ArgumentException("The pixels must fill a square of the given size, four bytes each.", nameof(pixels));
        }

        var header = new BITMAPV5HEADER
        {
            bV5Size = (uint)sizeof(BITMAPV5HEADER),
            bV5Width = size,
            // Negative: the rows run top to bottom.
            bV5Height = -size,
            bV5Planes = 1,
            bV5BitCount = 32,
            bV5Compression = BI_COMPRESSION.BI_BITFIELDS,
            bV5RedMask = 0x00FF0000,
            bV5GreenMask = 0x0000FF00,
            bV5BlueMask = 0x000000FF,
            bV5AlphaMask = 0xFF000000,
        };
        void* bits;
        var color = PInvoke.CreateDIBSection(default, (BITMAPINFO*)&header, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
        if (color.IsNull)
        {
            throw new COMException("Windows could not make the icon's picture.");
        }

        // The mask is unused for an icon with alpha, but an icon must have one.
        var mask = PInvoke.CreateBitmap(size, size, 1, 1, null);
        try
        {
            pixels.CopyTo(new Span<byte>(bits, pixels.Length));
            var info = new ICONINFO { fIcon = true, hbmColor = color, hbmMask = mask };
            var icon = PInvoke.CreateIconIndirect(&info);
            return icon.IsNull ? throw new COMException("Windows could not make the icon.") : new TaskbarIcon(icon, size);
        }
        finally
        {
            // The icon holds copies of both.
            PInvoke.DeleteObject(color);
            PInvoke.DeleteObject(mask);
        }
    }

    public void Dispose()
    {
        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }
}
