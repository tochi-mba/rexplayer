using System.Runtime.InteropServices;
using Windows.Win32.Graphics.Direct3D11;

namespace Rex.Media.Interop.Graphics;

/// <summary>
/// How long work takes on the graphics card itself, read with Direct3D 11 timestamp queries: the
/// card writes a time before and after the work, and a disjoint query says whether its clock held
/// steady between them. Call sequencing only; what to time and what the numbers mean are the
/// caller's (the performance suite measures each picture effect this way).
/// </summary>
public sealed unsafe partial class D3D11VideoRenderer
{
    // ID3D11DeviceContext's Begin, End and GetData in its vtable: IUnknown's three, ID3D11DeviceChild's
    // four, then the context's own from VSSetConstantBuffers (7) on. GetData is called directly because
    // its "not ready yet" answer is a success code the generated wrapper cannot report.
    private const int BeginSlot = 27;
    private const int EndSlot = 28;
    private const int GetDataSlot = 29;

    private const int NotReady = 1;

    /// <summary>
    /// Runs <paramref name="work"/> (draws on this renderer) and gives how long the graphics card
    /// spent on it, in milliseconds; null when the card's clock changed speed meanwhile, so the time
    /// cannot be trusted. Waits for the card to finish the work.
    /// </summary>
    public double? TimeOnGpu(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var disjoint = Query(D3D11_QUERY.D3D11_QUERY_TIMESTAMP_DISJOINT);
        var start = Query(D3D11_QUERY.D3D11_QUERY_TIMESTAMP);
        var end = Query(D3D11_QUERY.D3D11_QUERY_TIMESTAMP);
        var context = Marshal.GetComInterfaceForObject<ID3D11DeviceContext, ID3D11DeviceContext>(_context);
        try
        {
            var vtable = *(nint**)context;
            var begin = (delegate* unmanaged[Stdcall]<nint, nint, void>)vtable[BeginSlot];
            var finish = (delegate* unmanaged[Stdcall]<nint, nint, void>)vtable[EndSlot];
            var getData = (delegate* unmanaged[Stdcall]<nint, nint, void*, uint, uint, int>)vtable[GetDataSlot];
            begin(context, disjoint);
            finish(context, start);
            work();
            finish(context, end);
            finish(context, disjoint);
            _context.Flush();

            D3D11_QUERY_DATA_TIMESTAMP_DISJOINT clock;
            ulong before, after;
            Wait(getData, context, disjoint, &clock, (uint)sizeof(D3D11_QUERY_DATA_TIMESTAMP_DISJOINT));
            Wait(getData, context, start, &before, sizeof(ulong));
            Wait(getData, context, end, &after, sizeof(ulong));
            return clock.Disjoint || clock.Frequency == 0 ? null : (after - before) * 1000.0 / clock.Frequency;
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(end);
            Marshal.Release(start);
            Marshal.Release(disjoint);
        }
    }

    /// <summary>Compiles (or fetches) the YUV shader of each effect in <paramref name="effects"/>, side by side, before a renderer draws them.</summary>
    public static void PrepareEffects(IEnumerable<int> effects) =>
        Parallel.ForEach(effects, effect => Code("yuv", "ps_4_0", effect));

    /// <summary>A new query of <paramref name="kind"/>, as a raw reference the caller releases.</summary>
    private nint Query(D3D11_QUERY kind)
    {
        var description = new D3D11_QUERY_DESC { Query = kind };
        ID3D11Query_unmanaged* query;
        _device.CreateQuery(&description, &query);
        return (nint)query;
    }

    /// <summary>Asks for a query's answer until the card has it.</summary>
    private static void Wait(delegate* unmanaged[Stdcall]<nint, nint, void*, uint, uint, int> getData, nint context, nint query, void* data, uint size)
    {
        int result;
        while ((result = getData(context, query, data, size, 0)) == NotReady)
        {
            Thread.Yield();
        }

        Marshal.ThrowExceptionForHR(result);
    }
}
