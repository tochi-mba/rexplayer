using Rex.Media.Engine;

namespace Rex.Media.AppCore.Transcription;

/// <summary>Owns a caption pause until the asynchronous player has acknowledged it.</summary>
public sealed class SubtitleHold
{
    private bool _pauseObserved;
    private bool _awaitingCoverage;

    public bool Holding { get; private set; }
    public bool Overridden { get; private set; }

    public void Reset() => (Holding, Overridden, _pauseObserved, _awaitingCoverage) = (false, false, false, false);

    /// <summary>
    /// Another transcript of the same media is being loaded (a new quality or edition): waiting
    /// is allowed again, and a wait in progress is kept, unreleased, until <see cref="CoverageReady"/>,
    /// because until then nothing is known about the new coverage and the wait would end by mistake.
    /// </summary>
    public void AwaitCoverage() => (Overridden, _awaitingCoverage) = (false, Holding);

    /// <summary>The new transcript's coverage is known: the wait is judged against it again.</summary>
    public void CoverageReady() => _awaitingCoverage = false;

    public void PlayNow() => (Holding, Overridden, _pauseObserved, _awaitingCoverage) = (false, true, false, false);

    /// <summary>Returns transport work only for stable states, never mistaking a pending pause or seek for Play.</summary>
    public HoldAction Update(bool allowed, SessionState state, HoldAction desired)
    {
        if (!allowed)
        {
            Overridden = false;
        }

        if (state is SessionState.Idle or SessionState.Stopping or SessionState.Ended or SessionState.Faulted)
        {
            Reset();
            return HoldAction.Nothing;
        }

        // Play now waives only the gap the viewer chose to cross. Seeking is a new choice,
        // and reaching ready captions rearms waiting for a later gap.
        if (Overridden && (state == SessionState.Seeking || desired != HoldAction.Hold))
        {
            Overridden = false;
        }

        if (Holding)
        {
            if (state == SessionState.Paused)
            {
                _pauseObserved = true;
                if (!_awaitingCoverage && (!allowed || desired == HoldAction.Release))
                {
                    Holding = false;
                    return HoldAction.Release;
                }
            }
            else if (_pauseObserved && state is SessionState.Playing or SessionState.Buffering)
            {
                PlayNow();
            }

            return HoldAction.Nothing;
        }

        if (allowed && !Overridden && desired == HoldAction.Hold && state is SessionState.Playing or SessionState.Buffering)
        {
            Holding = true;
            _pauseObserved = false;
            return HoldAction.Hold;
        }

        return HoldAction.Nothing;
    }
}
