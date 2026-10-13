using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.WinRT;

namespace Rex.Media.Interop.Shell;

/// <summary>
/// The few Windows Runtime calls rexplayer makes, at the binary interface, without the runtime's
/// language projection: this assembly targets plain Windows, so the engine's adapters can use it.
/// Interface pointers are raw; every one this class hands out is owned by the caller, who releases it.
/// Each interface's methods are numbered as Windows' metadata (C:\Windows\System32\WinMetadata) lists
/// them, after the six of IInspectable.
/// </summary>
internal static unsafe class WinRt
{
    public static readonly Guid IUnknown = new("00000000-0000-0000-C000-000000000046");
    public static readonly Guid IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");

    /// <summary>Calls method <paramref name="slot"/> of <paramref name="target"/> that takes nothing more and returns an HRESULT.</summary>
    public static int Call(nint target, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Method(target, slot))(target);

    public static void Check(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    /// <summary>Method <paramref name="slot"/> of the interface <paramref name="target"/> points at.</summary>
    public static void* Method(nint target, int slot) => (*(void***)target)[slot];

    public static void SetBoolean(nint target, int slot, bool value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Method(target, slot))(target, value ? (byte)1 : (byte)0));

    public static bool GetBoolean(nint target, int slot)
    {
        byte value;
        Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Method(target, slot))(target, &value));
        return value != 0;
    }

    public static void SetInt32(nint target, int slot, int value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Method(target, slot))(target, value));

    public static int GetInt32(nint target, int slot)
    {
        int value;
        Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(target, slot))(target, &value));
        return value;
    }

    /// <summary>Sets a time span: the runtime's TimeSpan is a count of 100-nanosecond ticks, as .NET's is.</summary>
    public static void SetTime(nint target, int slot, TimeSpan value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Method(target, slot))(target, value.Ticks));

    public static TimeSpan GetTime(nint target, int slot)
    {
        long ticks;
        Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Method(target, slot))(target, &ticks));
        return TimeSpan.FromTicks(ticks);
    }

    /// <summary>Sets a string property, through a runtime string made for the call.</summary>
    public static void SetString(nint target, int slot, string value)
    {
        using var text = Create(value);
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Method(target, slot))(target, text.Handle));
    }

    /// <summary>Calls a getter that returns an interface pointer, which the caller then owns.</summary>
    public static nint GetObject(nint target, int slot)
    {
        nint value;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(target, slot))(target, &value));
        return value;
    }

    /// <summary>Calls a method that takes one interface pointer.</summary>
    public static void SetObject(nint target, int slot, nint value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Method(target, slot))(target, value));

    /// <summary>Asks <paramref name="target"/> for interface <paramref name="iid"/>; the caller owns the result.</summary>
    public static nint As(nint target, in Guid iid)
    {
        Check(Marshal.QueryInterface(target, in iid, out var result));
        return result;
    }

    public static void Release(ref nint target)
    {
        if (target != 0)
        {
            Marshal.Release(target);
            target = 0;
        }
    }

    /// <summary>The activation factory of <paramref name="runtimeClass"/> as interface <paramref name="iid"/>.</summary>
    public static nint Factory(string runtimeClass, in Guid iid)
    {
        using var name = Create(runtimeClass);
        fixed (Guid* wanted = &iid)
        {
            Check(PInvoke.RoGetActivationFactory(new HSTRING(name.Handle), wanted, out var factory));
            return FromRuntimeCallable(factory, iid);
        }
    }

    /// <summary>A new <paramref name="runtimeClass"/>, made with its default constructor, as interface <paramref name="iid"/>.</summary>
    public static nint Activate(string runtimeClass, in Guid iid)
    {
        using var name = Create(runtimeClass);
        Check(PInvoke.RoActivateInstance(new HSTRING(name.Handle), out var instance));
        return FromRuntimeCallable(instance, iid);
    }

    /// <summary>The raw interface <paramref name="iid"/> of an object .NET wrapped, letting the wrapper go.</summary>
    public static nint FromRuntimeCallable(object wrapper, in Guid iid)
    {
        var unknown = Marshal.GetIUnknownForObject(wrapper);
        try
        {
            return As(unknown, iid);
        }
        finally
        {
            Marshal.Release(unknown);
            Marshal.ReleaseComObject(wrapper);
        }
    }

    private static HString Create(string value)
    {
        Check(PInvoke.WindowsCreateString(value, (uint)value.Length, out var handle));
        return new HString(handle);
    }

    /// <summary>A runtime string, deleted when disposed.</summary>
    private readonly struct HString(SafeHandle handle) : IDisposable
    {
        public nint Handle => handle.DangerousGetHandle();

        public void Dispose() => handle.Dispose();
    }
}
