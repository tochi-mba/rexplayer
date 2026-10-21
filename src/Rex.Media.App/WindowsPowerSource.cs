using System.Runtime.InteropServices;
using Microsoft.Windows.System.Power;
using Rex.Media.AppCore.Performance;

namespace Rex.Media.App;

/// <summary>
/// The power as Windows reports it: the battery and energy saver from <see cref="PowerManager"/>,
/// watched only while someone listens. REXPLAYER_FAKE_ENERGY_SAVER=on (or off) stands in for the
/// energy saver status on machines whose status a test cannot change, the way REXPLAYER_FAKE_AUDIO
/// stands in for a sound device. Every read survives a machine that cannot answer (a COM failure
/// reads as a charger and no energy saver).
/// </summary>
internal sealed class WindowsPowerSource : IPowerSource
{
    private readonly bool? _fakeEnergySaver;
    private EventHandler? _changed;
    private bool _watching;

    private WindowsPowerSource(bool? fakeEnergySaver) => _fakeEnergySaver = fakeEnergySaver;

    /// <summary>The real source, with the energy saver status forced when the environment asks.</summary>
    public static IPowerSource Create() => new WindowsPowerSource(Environment.GetEnvironmentVariable("REXPLAYER_FAKE_ENERGY_SAVER") switch
    {
        { } fake when fake.Equals("on", StringComparison.OrdinalIgnoreCase) => true,
        { } fake when fake.Equals("off", StringComparison.OrdinalIgnoreCase) => false,
        _ => (bool?)null,
    });

    public bool OnBattery
    {
        get
        {
            try
            {
                return PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent && PowerManager.BatteryStatus != BatteryStatus.NotPresent;
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    public bool EnergySaver
    {
        get
        {
            if (_fakeEnergySaver is { } fake)
            {
                return fake;
            }

            try
            {
                return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    public event EventHandler? Changed
    {
        add
        {
            _changed += value;
            if (!_watching)
            {
                try
                {
                    PowerManager.EnergySaverStatusChanged += OnChanged;
                    PowerManager.PowerSupplyStatusChanged += OnChanged;
                    _watching = true;
                }
                catch (COMException ex)
                {
                    App.Log.Debug("window", "The power cannot be watched: " + ex.Message);
                }
            }
        }
        remove
        {
            _changed -= value;
            if (_changed is null && _watching)
            {
                PowerManager.EnergySaverStatusChanged -= OnChanged;
                PowerManager.PowerSupplyStatusChanged -= OnChanged;
                _watching = false;
            }
        }
    }

    private void OnChanged(object? sender, object e) => _changed?.Invoke(this, EventArgs.Empty);
}
