using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rex.Media.Interop.Graphics;

/// <summary>
/// Hands a swap chain to a XAML swap-chain panel through the panel's native interface
/// (ISwapChainPanelNative, from the Windows App SDK's microsoft.ui.xaml.media.dxinterop.h). It works
/// on raw COM pointers, so this project needs no reference to the UI framework: the app passes the
/// panel's IUnknown and the swap chain from <see cref="D3D11VideoRenderer.AttachComposition"/>.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public static unsafe class SwapChainPanelNative
{
    private static readonly Guid InterfaceId = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    /// <summary>
    /// Shows <paramref name="swapChain"/> (or nothing, when it is zero) in the panel. Must be called
    /// on the panel's UI thread. Neither pointer's reference is taken over.
    /// </summary>
    public static void SetSwapChain(nint panel, nint swapChain)
    {
        if (panel == 0)
        {
            throw new ArgumentException("There is no panel.", nameof(panel));
        }

        var id = InterfaceId;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(panel, in id, out var native));
        try
        {
            // The interface's one method after IUnknown's three: SetSwapChain(IDXGISwapChain*).
            var setSwapChain = (delegate* unmanaged[Stdcall]<nint, nint, int>)(*(void***)native)[3];
            Marshal.ThrowExceptionForHR(setSwapChain(native, swapChain));
        }
        finally
        {
            Marshal.Release(native);
        }
    }
}
