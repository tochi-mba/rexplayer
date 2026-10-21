using Rex.Media.Settings;

namespace Rex.Media.AppCore.Performance;

/// <summary>
/// The performance mode in effect while Windows' energy saver comes and goes. The user's saved
/// choice (<see cref="Chosen"/>) is never rewritten: while energy saver is on and the user allows
/// it, a chosen Best quality, Balanced or Fastest gives way to Automatic, so the device budget
/// adapts, and the choice returns when energy saver goes off. A mode chosen in Preferences while
/// the override is on wins (<see cref="Held"/>) until energy saver next changes. A notice is worded
/// only when the mode in effect actually changes, so a flickering status says nothing twice.
/// </summary>
/// <param name="Chosen">The saved choice from Preferences.</param>
/// <param name="Allowed">Preferences, Performance: use Automatic while energy saver is on.</param>
/// <param name="EnergySaver">Windows' energy saver is on, as last seen.</param>
/// <param name="Held">The user chose a mode while the override was on; it stands until energy saver changes.</param>
public sealed record EnergySaverMode(PerformanceChoice Chosen, bool Allowed, bool EnergySaver, bool Held = false)
{
    /// <summary>What the notice says when the override starts.</summary>
    public const string StartedNotice = "Energy Saver is on, so rexplayer is using the Automatic performance mode";

    /// <summary>The state before the energy saver status has been read: apply <see cref="PowerChanged"/> with the first reading.</summary>
    public static EnergySaverMode Start(PerformanceChoice chosen, bool allowed) => new(chosen, allowed, false);

    /// <summary>Automatic is in effect in place of the saved choice.</summary>
    public bool Overriding => Allowed && EnergySaver && !Held && Chosen != PerformanceChoice.Automatic;

    /// <summary>The mode everything consuming the performance mode uses.</summary>
    public PerformanceChoice Effective => Overriding ? PerformanceChoice.Automatic : Chosen;

    /// <summary>
    /// Energy saver was read as <paramref name="energySaver"/>: the state after it, and the notice to
    /// show when the mode in effect changed, else null. The same status again changes nothing.
    /// </summary>
    public (EnergySaverMode Next, string? Notice) PowerChanged(bool energySaver)
    {
        if (energySaver == EnergySaver)
        {
            return (this, null);
        }

        var next = this with { EnergySaver = energySaver, Held = false };
        var notice = next.Overriding == Overriding ? null
            : next.Overriding ? StartedNotice
            : $"Energy Saver is off, back to {Name(Chosen)}";
        return (next, notice);
    }

    /// <summary>Preferences saved: a different mode chosen while the override is on holds until energy saver changes.</summary>
    public EnergySaverMode WithChoice(PerformanceChoice chosen, bool allowed) =>
        this with { Chosen = chosen, Allowed = allowed, Held = Held || (Overriding && chosen != Chosen) };

    /// <summary>One plain sentence on the mode in effect and why, for Preferences and the log.</summary>
    public string Describe(DeviceFacts facts) =>
        Overriding ? "Automatic, because Energy Saver is on. " + DeviceProfile.Describe(PerformanceChoice.Automatic, facts) : DeviceProfile.Describe(Effective, facts);

    private static string Name(PerformanceChoice choice) => choice switch
    {
        PerformanceChoice.Balanced => DeviceProfile.Name(PerformanceTier.Balanced),
        PerformanceChoice.Fastest => DeviceProfile.Name(PerformanceTier.Fastest),
        _ => DeviceProfile.Name(PerformanceTier.BestQuality),
    };
}
