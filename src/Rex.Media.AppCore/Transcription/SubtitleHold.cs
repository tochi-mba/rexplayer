using Rex.Media.Engine;

namespace Rex.Media.AppCore.Transcription;

/// <summary>Owns a caption pause until the asynchronous player has acknowledged it.</summary>
public sealed class SubtitleHold
{
    private bool _pauseObserved;

    public bool Holding { get; private set; }
    public bool Overridden { get; private set; }

    public void Reset() => (Holding, Overridden, _pauseObserved) = (false, false, false);

    public void AllowWaiting() => Overridden = false;

    public void PlayNow() => (Holding, Overridden, _pauseObserved) = (false, true, false);

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

        if (Holding)
        {
            if (state == SessionState.Paused)
            {
                _pauseObserved = true;
                if (!allowed || desired == HoldAction.Release)
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
