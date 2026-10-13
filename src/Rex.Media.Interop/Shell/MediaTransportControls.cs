using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.WinRT;

namespace Rex.Media.Interop.Shell;

/// <summary>A button of Windows' media controls (Windows' own numbering).</summary>
public enum MediaControlButton
{
    Play = 0,
    Pause = 1,
    Stop = 2,
    Record = 3,
    FastForward = 4,
    Rewind = 5,
    Next = 6,
    Previous = 7,
    ChannelUp = 8,
    ChannelDown = 9,
}

/// <summary>
/// Windows' media controls for one window (its System Media Transport Controls): the media keys,
/// the card in the volume flyout and on the lock screen. This class only passes on what it is told
/// and what is pressed; what to show is decided elsewhere. Made and used on one thread; button
/// presses and position requests arrive on Windows' threads.
/// </summary>
public sealed unsafe class MediaTransportControls : IDisposable
{
    private static readonly Guid InteropIid = new("DDB0472D-C911-4A1F-86D9-DC3D71A95F5A");
    private static readonly Guid ControlsIid = new("99FA3FF4-1742-42A6-902E-087D41F965EC");
    private static readonly Guid Controls2Iid = new("EA98D2F6-7F3C-4AF2-A586-72889808EFB1");
    private static readonly Guid MusicIid = new("6BBF0C59-D0A0-4D26-92A0-F978E1D18E7B");
    private static readonly Guid Music2Iid = new("00368462-97D3-44B9-B00F-008AFCEFAF18");
    private static readonly Guid VideoIid = new("5609FDB1-5D2D-4872-8170-45DEE5BC2F5C");
    private static readonly Guid ImageIid = new("CD0BC7EF-54E7-411F-9933-F0E98B0A96D2");
    private static readonly Guid TimelineIid = new("5125316A-C3A2-475B-8507-93534DC88F15");
    private static readonly Guid StreamIid = new("905A0FE1-BC53-11DF-8C49-001E4FC686DA");
    private static readonly Guid StreamReferenceStaticsIid = new("857309DC-3FBF-4E7D-986F-EF3B1A07A964");

    /// <summary>TypedEventHandler of the controls and their button-pressed arguments.</summary>
    internal static readonly Guid ButtonHandlerIid = new("0557E996-7B23-5BAE-AA81-EA0D671143A4");

    /// <summary>TypedEventHandler of the controls and a playback position change request.</summary>
    internal static readonly Guid PositionHandlerIid = new("44E34F15-BDC0-50A7-ACE4-39E91FB753F1");

    private nint _controls;
    private nint _controls2;
    private nint _display;
    private long _buttonToken;
    private long _positionToken;

    private MediaTransportControls(nint controls)
    {
        _controls = controls;
        _controls2 = WinRt.As(controls, Controls2Iid);
        _display = WinRt.GetObject(controls, Slot.DisplayUpdater);

        var buttons = EventHandlerObject.Create(ButtonHandlerIid, this, buttons: true);
        var position = EventHandlerObject.Create(PositionHandlerIid, this, buttons: false);
        try
        {
            long token;
            WinRt.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)WinRt.Method(_controls, Slot.AddButtonPressed))(_controls, buttons, &token));
            _buttonToken = token;
            WinRt.Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)WinRt.Method(_controls2, Slot2.AddPositionChangeRequested))(_controls2, position, &token));
            _positionToken = token;
        }
        finally
        {
            // Windows keeps its own reference to each handler while it is registered.
            Marshal.Release(buttons);
            Marshal.Release(position);
        }
    }

    /// <summary>Raised, on a Windows thread, when a media key or a button on Windows' card is pressed.</summary>
    public event EventHandler<MediaControlButton>? ButtonPressed;

    /// <summary>Raised, on a Windows thread, when Windows asks to move to a position (its card's timeline).</summary>
    public event EventHandler<TimeSpan>? PositionRequested;

    /// <summary>
    /// The controls of <paramref name="window"/>, a top-level window of this process. Throws
    /// <see cref="COMException"/> where Windows has none to give (a session without the media service).
    /// </summary>
    public static MediaTransportControls ForWindow(nint window)
    {
        var interop = WinRt.Factory("Windows.Media.SystemMediaTransportControls", InteropIid);
        try
        {
            nint controls;
            var iid = ControlsIid;
            WinRt.Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)WinRt.Method(interop, 6))(interop, window, &iid, &controls));
            try
            {
                return new MediaTransportControls(controls);
            }
            catch
            {
                Marshal.Release(controls);
                throw;
            }
        }
        finally
        {
            WinRt.Release(ref interop);
        }
    }

    /// <summary>Whether Windows lists this window's media at all; off, the keys and the card are someone else's.</summary>
    public bool IsEnabled
    {
        get => WinRt.GetBoolean(_controls, Slot.GetIsEnabled);
        set => WinRt.SetBoolean(_controls, Slot.PutIsEnabled, value);
    }

    public bool IsPlayEnabled => WinRt.GetBoolean(_controls, Slot.GetIsPlayEnabled);

    public bool IsPauseEnabled => WinRt.GetBoolean(_controls, Slot.GetIsPauseEnabled);

    public bool IsStopEnabled => WinRt.GetBoolean(_controls, Slot.GetIsStopEnabled);

    public bool IsPreviousEnabled => WinRt.GetBoolean(_controls, Slot.GetIsPreviousEnabled);

    public bool IsNextEnabled => WinRt.GetBoolean(_controls, Slot.GetIsNextEnabled);

    /// <summary>Windows' playback status: 0 closed, 1 changing, 2 stopped, 3 playing, 4 paused.</summary>
    public int Status
    {
        get => WinRt.GetInt32(_controls, Slot.GetPlaybackStatus);
        set => WinRt.SetInt32(_controls, Slot.PutPlaybackStatus, value);
    }

    /// <summary>Which buttons Windows offers; a key for a button that is off is not passed on.</summary>
    public void SetButtons(bool play, bool pause, bool stop, bool previous, bool next)
    {
        WinRt.SetBoolean(_controls, Slot.PutIsPlayEnabled, play);
        WinRt.SetBoolean(_controls, Slot.PutIsPauseEnabled, pause);
        WinRt.SetBoolean(_controls, Slot.PutIsStopEnabled, stop);
        WinRt.SetBoolean(_controls, Slot.PutIsPreviousEnabled, previous);
        WinRt.SetBoolean(_controls, Slot.PutIsNextEnabled, next);
    }

    /// <summary>
    /// Shows a card for media of <paramref name="kind"/> (1 music, 2 video, 3 picture) with a
    /// thumbnail encoded as PNG or JPEG, or none.
    /// </summary>
    public void SetDisplay(int kind, string title, string subtitle, string albumArtist, string albumTitle, int trackNumber, ReadOnlySpan<byte> thumbnail)
    {
        WinRt.Check(WinRt.Call(_display, Display.ClearAll));
        WinRt.SetInt32(_display, Display.PutType, kind);
        var properties = kind switch
        {
            1 => WinRt.GetObject(_display, Display.GetMusicProperties),
            2 => WinRt.GetObject(_display, Display.GetVideoProperties),
            3 => WinRt.GetObject(_display, Display.GetImageProperties),
            _ => 0,
        };
        try
        {
            switch (kind)
            {
                case 1:
                    WinRt.SetString(properties, 7, title);
                    WinRt.SetString(properties, 9, albumArtist);
                    WinRt.SetString(properties, 11, subtitle);
                    var music2 = WinRt.As(properties, Music2Iid);
                    try
                    {
                        WinRt.SetString(music2, 7, albumTitle);
                        WinRt.SetInt32(music2, 9, Math.Max(0, trackNumber));
                    }
                    finally
                    {
                        WinRt.Release(ref music2);
                    }

                    break;
                case 2:
                case 3:
                    var typed = WinRt.As(properties, kind == 2 ? VideoIid : ImageIid);
                    try
                    {
                        WinRt.SetString(typed, 7, title);
                        WinRt.SetString(typed, 9, subtitle);
                    }
                    finally
                    {
                        WinRt.Release(ref typed);
                    }

                    break;
            }

            if (!thumbnail.IsEmpty)
            {
                SetThumbnail(thumbnail);
            }

            WinRt.Check(WinRt.Call(_display, Display.Update));
        }
        finally
        {
            WinRt.Release(ref properties);
        }
    }

    /// <summary>Takes the card away.</summary>
    public void ClearDisplay()
    {
        WinRt.Check(WinRt.Call(_display, Display.ClearAll));
        WinRt.Check(WinRt.Call(_display, Display.Update));
    }

    /// <summary>
    /// The timeline: <paramref name="position"/> of <paramref name="duration"/> (zero for media with no
    /// known length), and whether Windows may ask to move within it.
    /// </summary>
    public void SetTimeline(TimeSpan position, TimeSpan duration, bool seekable)
    {
        var timeline = WinRt.Activate("Windows.Media.SystemMediaTransportControlsTimelineProperties", TimelineIid);
        try
        {
            WinRt.SetTime(timeline, 7, TimeSpan.Zero);
            WinRt.SetTime(timeline, 9, duration);
            WinRt.SetTime(timeline, 11, TimeSpan.Zero);
            WinRt.SetTime(timeline, 13, seekable ? duration : TimeSpan.Zero);
            WinRt.SetTime(timeline, 15, position);
            WinRt.SetObject(_controls2, Slot2.UpdateTimelineProperties, timeline);
        }
        finally
        {
            WinRt.Release(ref timeline);
        }
    }

    /// <summary>Takes the controls back from Windows: the card goes and the keys go to other programs.</summary>
    public void Dispose()
    {
        if (_controls == 0)
        {
            return;
        }

        try
        {
            ((delegate* unmanaged[Stdcall]<nint, long, int>)WinRt.Method(_controls, Slot.RemoveButtonPressed))(_controls, _buttonToken);
            ((delegate* unmanaged[Stdcall]<nint, long, int>)WinRt.Method(_controls2, Slot2.RemovePositionChangeRequested))(_controls2, _positionToken);
            WinRt.Call(_display, Display.ClearAll);
            WinRt.Call(_display, Display.Update);
            ((delegate* unmanaged[Stdcall]<nint, int, int>)WinRt.Method(_controls, Slot.PutPlaybackStatus))(_controls, 0);
            ((delegate* unmanaged[Stdcall]<nint, byte, int>)WinRt.Method(_controls, Slot.PutIsEnabled))(_controls, 0);
        }
        finally
        {
            WinRt.Release(ref _display);
            WinRt.Release(ref _controls2);
            WinRt.Release(ref _controls);
        }
    }

    /// <summary>A thumbnail from encoded picture bytes, through a stream Windows reads when it draws the card.</summary>
    private void SetThumbnail(ReadOnlySpan<byte> encoded)
    {
        var memory = PInvoke.SHCreateMemStream(encoded) ?? throw new COMException("Windows could not hold the thumbnail.");
        nint stream = 0;
        nint statics = 0;
        nint reference = 0;
        try
        {
            var iid = StreamIid;
            WinRt.Check(PInvoke.CreateRandomAccessStreamOverStream(memory, BSOS_OPTIONS.BSOS_DEFAULT, &iid, out var wrapped));
            stream = WinRt.FromRuntimeCallable(wrapped, StreamIid);
            statics = WinRt.Factory("Windows.Storage.Streams.RandomAccessStreamReference", StreamReferenceStaticsIid);
            WinRt.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)WinRt.Method(statics, 8))(statics, stream, &reference));
            WinRt.SetObject(_display, Display.PutThumbnail, reference);
        }
        finally
        {
            WinRt.Release(ref reference);
            WinRt.Release(ref statics);
            WinRt.Release(ref stream);
            Marshal.ReleaseComObject(memory);
        }
    }

    private void OnButton(int button) => ButtonPressed?.Invoke(this, (MediaControlButton)button);

    private void OnPosition(TimeSpan position) => PositionRequested?.Invoke(this, position);

    /// <summary>ISystemMediaTransportControls, as Windows' metadata numbers its methods.</summary>
    private static class Slot
    {
        public const int GetPlaybackStatus = 6;
        public const int PutPlaybackStatus = 7;
        public const int DisplayUpdater = 8;
        public const int GetIsEnabled = 10;
        public const int PutIsEnabled = 11;
        public const int GetIsPlayEnabled = 12;
        public const int PutIsPlayEnabled = 13;
        public const int GetIsStopEnabled = 14;
        public const int PutIsStopEnabled = 15;
        public const int GetIsPauseEnabled = 16;
        public const int PutIsPauseEnabled = 17;
        public const int GetIsPreviousEnabled = 24;
        public const int PutIsPreviousEnabled = 25;
        public const int GetIsNextEnabled = 26;
        public const int PutIsNextEnabled = 27;
        public const int AddButtonPressed = 32;
        public const int RemoveButtonPressed = 33;
    }

    /// <summary>ISystemMediaTransportControls2.</summary>
    private static class Slot2
    {
        public const int UpdateTimelineProperties = 12;
        public const int AddPositionChangeRequested = 13;
        public const int RemovePositionChangeRequested = 14;
    }

    /// <summary>ISystemMediaTransportControlsDisplayUpdater.</summary>
    private static class Display
    {
        public const int PutType = 7;
        public const int PutThumbnail = 11;
        public const int GetMusicProperties = 12;
        public const int GetVideoProperties = 13;
        public const int GetImageProperties = 14;
        public const int ClearAll = 16;
        public const int Update = 17;
    }

    /// <summary>
    /// A Windows Runtime event handler made by hand: an object with a reference count and a table of
    /// four methods (QueryInterface, AddRef, Release, Invoke) that calls back into the controls. It is
    /// agile, so Windows calls it from whichever thread it likes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventHandlerObject
    {
        private static readonly nint* Table = CreateTable();

        public nint Methods;
        public int References;
        public nint Target;
        public Guid Iid;
        public int IsButtons;

        /// <summary>A new handler for <paramref name="target"/>, with one reference the caller owns.</summary>
        public static nint Create(Guid iid, MediaTransportControls target, bool buttons)
        {
            var handler = (EventHandlerObject*)NativeMemory.AllocZeroed((nuint)sizeof(EventHandlerObject));
            handler->Methods = (nint)Table;
            handler->References = 1;
            handler->Target = GCHandle.ToIntPtr(GCHandle.Alloc(target, GCHandleType.Weak));
            handler->Iid = iid;
            handler->IsButtons = buttons ? 1 : 0;
            return (nint)handler;
        }

        private static nint* CreateTable()
        {
            var table = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 4));
            table[0] = (nint)(delegate* unmanaged[Stdcall]<EventHandlerObject*, Guid*, nint*, int>)&QueryInterface;
            table[1] = (nint)(delegate* unmanaged[Stdcall]<EventHandlerObject*, uint>)&AddRef;
            table[2] = (nint)(delegate* unmanaged[Stdcall]<EventHandlerObject*, uint>)&Release;
            table[3] = (nint)(delegate* unmanaged[Stdcall]<EventHandlerObject*, nint, nint, int>)&Invoke;
            return table;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterface(EventHandlerObject* self, Guid* iid, nint* result)
        {
            if (*iid == self->Iid || *iid == WinRt.IUnknown || *iid == WinRt.IAgileObject)
            {
                Interlocked.Increment(ref self->References);
                *result = (nint)self;
                return 0;
            }

            *result = 0;
            return unchecked((int)0x80004002);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(EventHandlerObject* self) => (uint)Interlocked.Increment(ref self->References);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint Release(EventHandlerObject* self)
        {
            var left = Interlocked.Decrement(ref self->References);
            if (left == 0)
            {
                GCHandle.FromIntPtr(self->Target).Free();
                NativeMemory.Free(self);
            }

            return (uint)left;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(EventHandlerObject* self, nint sender, nint arguments)
        {
            try
            {
                if (GCHandle.FromIntPtr(self->Target).Target is not MediaTransportControls target || arguments == 0)
                {
                    return 0;
                }

                if (self->IsButtons != 0)
                {
                    target.OnButton(WinRt.GetInt32(arguments, 6));
                }
                else
                {
                    target.OnPosition(WinRt.GetTime(arguments, 6));
                }

                return 0;
            }
#pragma warning disable CA1031 // Nothing may cross back into Windows' thread; the press is dropped.
            catch (Exception)
#pragma warning restore CA1031
            {
                return 0;
            }
        }
    }
}
