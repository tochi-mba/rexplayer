namespace Rex.Media.AppCore.Performance;

/// <summary>
/// Where the power comes from, as the window needs it: whether the computer runs on its battery
/// and whether Windows' energy saver (battery saver) is on, with one event for a change of either.
/// The window reads a real source; tests put a fake behind the same contract.
/// </summary>
public interface IPowerSource
{
    /// <summary>The computer runs on its battery, not a charger.</summary>
    bool OnBattery { get; }

    /// <summary>Windows' energy saver is on.</summary>
    bool EnergySaver { get; }

    /// <summary>Raised on any thread when the power supply or energy saver changes.</summary>
    event EventHandler? Changed;
}
