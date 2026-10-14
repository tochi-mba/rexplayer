using System.Runtime.InteropServices;
using Windows.Win32.Graphics.Dxgi;

namespace Rex.Media.Interop.Graphics;

/// <summary>What the graphics adapter says of itself, for the device profile to classify (Rex.Media.AppCore.Performance).</summary>
public sealed partial class D3D11VideoRenderer
{
    /// <summary>The adapter's vendor id (0x1414 for the software rasteriser) and its own video memory in bytes.</summary>
    public (uint VendorId, long DedicatedVideoMemory) AdapterIdentity
    {
        get
        {
            ((IDXGIDevice)_device).GetAdapter(out var adapter);
            try
            {
                var description = adapter.GetDesc();
                return (description.VendorId, (long)(ulong)description.DedicatedVideoMemory);
            }
            finally
            {
                Marshal.ReleaseComObject(adapter);
            }
        }
    }
}
