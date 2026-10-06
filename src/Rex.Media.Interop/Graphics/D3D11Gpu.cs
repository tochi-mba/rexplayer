using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32.Graphics.Direct3D11;

namespace Rex.Media.Interop.Graphics;

/// <summary>
/// A Direct3D 11 device shared by a renderer and the decoders feeding it, so decoded pictures stay on
/// the graphics card from decoder to screen. The renderer owns it; decoders only borrow it.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11Gpu
{
    internal D3D11Gpu(ID3D11Device device, bool software)
    {
        Device = device;
        IsSoftware = software;
    }

    /// <summary>True for the software rasteriser (WARP), which has no video decoding of its own.</summary>
    public bool IsSoftware { get; }

    internal ID3D11Device Device { get; }
}

/// <summary>
/// A decoded picture that lives on the graphics card: a slice of a decoder's texture array. It keeps
/// the decoder's sample until disposed, because the decoder reuses the slice once the sample goes.
/// </summary>
[SupportedOSPlatform("windows8.0")]
public sealed class D3D11Surface : IDisposable
{
    private readonly object _sample;
    private bool _disposed;

    internal D3D11Surface(ID3D11Texture2D texture, uint index, object sample)
    {
        Texture = texture;
        Index = index;
        _sample = sample;
    }

    internal ID3D11Texture2D Texture { get; }

    internal uint Index { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Marshal.ReleaseComObject(Texture);
        Marshal.ReleaseComObject(_sample);
    }
}
