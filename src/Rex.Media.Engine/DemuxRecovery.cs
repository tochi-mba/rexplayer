using Rex.Media.Primitives;

namespace Rex.Media.Engine;

/// <summary>What the demux thread does after the demuxer refused to read on.</summary>
internal enum RecoveryStep
{
    /// <summary>Read again: the demuxer has usually moved past the bytes it refused.</summary>
    ReadAgain,

    /// <summary>Seek past the damage, to <see cref="DemuxRecovery.Target"/>, and play on from there.</summary>
    SeekPast,

    /// <summary>Nothing more can be read: the stream ends here.</summary>
    GiveUp,
}

/// <summary>
/// The policy for a demuxer that throws part-way through a file. Most demuxers find their place
/// again by themselves, so the first refusals only read again; then the demux thread seeks past the
/// damage, a second further, then two, four and so on, which stays bounded (a dozen tries cover a
/// day) and keeps every try cheap. Packets from before the place it resumed at are dropped, and
/// pictures until the next keyframe, so nothing plays twice and no picture is drawn from a broken
/// reference.
/// </summary>
internal sealed class DemuxRecovery
{
    /// <summary>Refusals in a row that only read again before seeking past the damage.</summary>
    public const int ReadsAgain = 2;

    private static readonly MediaTime FirstStep = MediaTime.FromSeconds(1);

    private int _failures;
    private MediaTime _resumeAt = MediaTime.Unknown;
    private bool _awaitKeyframe;

    /// <summary>The latest time read, from which a seek past damage is measured.</summary>
    public MediaTime Reached { get; private set; } = MediaTime.Unknown;

    /// <summary>Where the last <see cref="RecoveryStep.SeekPast"/> seeks to.</summary>
    public MediaTime Target { get; private set; } = MediaTime.Unknown;

    /// <summary>Whether the file has refused to read at all since it opened: the first time is worth telling.</summary>
    public bool Damaged { get; private set; }

    /// <summary>The next step after a refusal, in a file of <paramref name="duration"/> that can seek or not.</summary>
    public RecoveryStep Failed(MediaTime duration, bool seekable)
    {
        Damaged = true;
        _failures++;
        if (_failures <= ReadsAgain)
        {
            return RecoveryStep.ReadAgain;
        }

        var from = Reached.IsKnown ? Reached : MediaTime.Zero;
        var target = from + new MediaTime(FirstStep.Ticks << Math.Min(_failures - ReadsAgain - 1, 30));
        if (!seekable || !duration.IsKnown || target >= duration)
        {
            return RecoveryStep.GiveUp;
        }

        Target = target;
        _resumeAt = target;
        _awaitKeyframe = true;
        return RecoveryStep.SeekPast;
    }

    /// <summary>
    /// Whether a packet read after a refusal is played: false for one from before the place reading
    /// resumed at, and for pictures until a keyframe. A packet that is played ends the run of refusals.
    /// </summary>
    public bool Keep(MediaTime timestamp, bool video, bool keyframe)
    {
        if (_resumeAt.IsKnown && timestamp.IsKnown && timestamp < _resumeAt)
        {
            return false;
        }

        if (video && _awaitKeyframe)
        {
            if (!keyframe)
            {
                return false;
            }

            _awaitKeyframe = false;
        }

        _failures = 0;
        if (timestamp.IsKnown && (!Reached.IsKnown || timestamp > Reached))
        {
            Reached = timestamp;
        }

        return true;
    }

    /// <summary>A seek the user asked for, or the next item of a run: what was skipped before no longer matters.</summary>
    public void Restart()
    {
        _failures = 0;
        _resumeAt = MediaTime.Unknown;
        _awaitKeyframe = false;
        Reached = MediaTime.Unknown;
    }
}
