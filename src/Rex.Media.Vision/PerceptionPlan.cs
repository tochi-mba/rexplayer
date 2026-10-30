namespace Rex.Media.Vision;

/// <summary>How much of the camera's perception a computer is asked for (ADR-0022), as the performance tier decides.</summary>
public enum PerceptionLevel
{
    /// <summary>The matte and the hands every picture, the body every other: a separate graphics card, or a fast processor.</summary>
    Full,

    /// <summary>The matte and the hands every picture, the body every third: an ordinary laptop.</summary>
    Light,

    /// <summary>rexplayer's own matte, flow and guessed hands only; no model runs.</summary>
    HandWritten,
}

/// <summary>
/// Which models run on this picture. The detectors (palm, person) are expensive and only find
/// things; once a hand or the body is found, its model follows it from the last picture's points, and
/// the detector runs again only when it is lost, or now and then to catch a newcomer. A model that
/// has proved slow on this computer (<see cref="FrameBudget"/>) runs at half its rate, and one that
/// would take seconds never runs. Pure: the perceiver asks it, then does what it says.
/// </summary>
public sealed class PerceptionPlan
{
    /// <summary>With both hands followed, the palm detector still runs this often, for a hand that comes into view.</summary>
    public const int PalmSweepEvery = 30;

    /// <summary>With the body followed, the person detector still runs this often.</summary>
    public const int PersonSweepEvery = 60;

    private long _frame = -1;

    public PerceptionLevel Level { get; set; } = PerceptionLevel.Light;

    /// <summary>The camera's rate, which the budgets are judged against.</summary>
    public double FramesPerSecond { get; set; } = 30;

    public FrameBudget MatteBudget { get; } = new();

    public FrameBudget PalmBudget { get; } = new();

    public FrameBudget HandBudget { get; } = new();

    public FrameBudget PersonBudget { get; } = new();

    public FrameBudget PoseBudget { get; } = new();

    public bool RunMatte { get; private set; }

    public bool RunPalm { get; private set; }

    public bool RunHands { get; private set; }

    public bool RunPerson { get; private set; }

    public bool RunPose { get; private set; }

    /// <summary>Pictures planned so far.</summary>
    public long Frame => _frame;

    /// <summary>
    /// Plans the next picture: <paramref name="handsFollowed"/> is how many hands the last picture
    /// kept (0 to 2), <paramref name="bodyFollowed"/> whether the body was kept.
    /// </summary>
    public void Next(int handsFollowed, bool bodyFollowed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(handsFollowed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(handsFollowed, 2);
        _frame++;
        if (Level == PerceptionLevel.HandWritten)
        {
            (RunMatte, RunPalm, RunHands, RunPerson, RunPose) = (false, false, false, false, false);
            return;
        }

        var bodyEvery = Level == PerceptionLevel.Full ? 2 : 3;
        RunMatte = Due(MatteBudget, 1);
        RunPalm = Due(PalmBudget, handsFollowed < 2 ? (Level == PerceptionLevel.Full ? 1 : 2) : PalmSweepEvery);
        RunHands = Due(HandBudget, 1);
        RunPerson = Due(PersonBudget, bodyFollowed ? PersonSweepEvery : (Level == PerceptionLevel.Full ? 2 : 4));
        RunPose = Due(PoseBudget, bodyEvery);
    }

    /// <summary>Whether a model that should run every <paramref name="every"/> pictures runs on this one, slowed or stopped by its budget.</summary>
    private bool Due(FrameBudget budget, int every)
    {
        var use = budget.Decide(FramesPerSecond);
        if (use == ModelUse.Unsuitable)
        {
            return false;
        }

        if (use == ModelUse.OnDemand && budget.Samples > 0)
        {
            every *= 2;
        }

        return _frame % every == 0;
    }

    /// <summary>Starts counting pictures again; the budgets keep what they learned.</summary>
    public void Reset() => _frame = -1;
}
