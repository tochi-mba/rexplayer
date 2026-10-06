using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Rex.Media.Interop.Windowing;

/// <summary>
/// A plain top-level window for pictures, with its own thread running its message loop, so the
/// engine's video thread can draw into it at any time. Escape or the close button closes it. This is
/// the development host (rexplay play --window); the app's window is WinUI's.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed unsafe class VideoWindow : IDisposable
{
    private const uint EscapeKey = 0x1B;
    private const int MessageLoopReady = 10_000;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WNDPROC _procedure;
    private readonly string _className = "rexplayer-video-" + Guid.NewGuid().ToString("N");
    private HWND _window;
    private int _width;
    private int _height;
    private volatile bool _closed;
    private Exception? _failure;

    private VideoWindow(string title, int width, int height)
    {
        _procedure = Procedure;
        _thread = new Thread(() => Run(title, width, height)) { IsBackground = true, Name = "rexplayer window" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(MessageLoopReady) || _failure is not null)
        {
            throw new InvalidOperationException("The window could not be created.", _failure);
        }
    }

    /// <summary>Raised on the window's thread when the user closes it.</summary>
    public event EventHandler? Closed;

    public nint Handle => (nint)_window.Value;

    /// <summary>The size of the area pictures are drawn in.</summary>
    public (int Width, int Height) ClientSize => (Volatile.Read(ref _width), Volatile.Read(ref _height));

    public bool IsClosed => _closed;

    /// <summary>Opens a window whose drawing area is <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static VideoWindow Open(string title, int width, int height) => new(title, width, height);

    public void Dispose()
    {
        if (!_closed && _window != HWND.Null)
        {
            PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
        }

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join();
        }

        _ready.Dispose();
    }

    private void Run(string title, int width, int height)
    {
        try
        {
            var module = PInvoke.GetModuleHandle((string?)null);
            fixed (char* className = _className)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _procedure,
                    hInstance = (HINSTANCE)module.DangerousGetHandle(),
                    hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                    lpszClassName = className,
                };
                if (PInvoke.RegisterClassEx(windowClass) == 0)
                {
                    throw new InvalidOperationException("The window class could not be registered.");
                }
            }

            var style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_VISIBLE;
            var frame = new RECT { right = width, bottom = height };
            PInvoke.AdjustWindowRectEx(ref frame, style, false, 0);
            _window = PInvoke.CreateWindowEx(0, _className, title, style, PInvoke.CW_USEDEFAULT, PInvoke.CW_USEDEFAULT, frame.right - frame.left, frame.bottom - frame.top, default, null, module, null);
            if (_window == HWND.Null)
            {
                throw new InvalidOperationException("The window could not be created.");
            }

            PInvoke.GetClientRect(_window, out var client);
            (_width, _height) = (client.right - client.left, client.bottom - client.top);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            _failure = ex;
            _ready.Set();
            return;
        }

        _ready.Set();
        while (PInvoke.GetMessage(out var message, default, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        PInvoke.UnregisterClass(_className, null);
    }

    private LRESULT Procedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_SIZE:
                Volatile.Write(ref _width, (int)(lParam.Value & 0xFFFF));
                Volatile.Write(ref _height, (int)((lParam.Value >> 16) & 0xFFFF));
                return default;
            case PInvoke.WM_KEYDOWN when wParam.Value == EscapeKey:
                PInvoke.DestroyWindow(window);
                return default;
            case PInvoke.WM_CLOSE:
                PInvoke.DestroyWindow(window);
                return default;
            case PInvoke.WM_DESTROY:
                _closed = true;
                Closed?.Invoke(this, EventArgs.Empty);
                PInvoke.PostQuitMessage(0);
                return default;
            default:
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }
}
