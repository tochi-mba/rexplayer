using Rex.Media.AppCore.Commands;
using Rex.Media.Settings;

namespace Rex.Media.AppCore.Player;

/// <summary>What happens when the current item ends (PB-13).</summary>
public enum AfterItem
{
    /// <summary>The next item plays, as usual.</summary>
    Continue,

    /// <summary>Playing stops.</summary>
    Stop,

    /// <summary>The next item is opened, paused at its start.</summary>
    Pause,
}

public sealed partial class PlayerController
{
    /// <summary>The sleep timer turns the sound down over this long before it acts.</summary>
    public static readonly TimeSpan SleepFade = TimeSpan.FromSeconds(10);

    /// <summary>The sleep timer's lengths offered in the menu, in minutes.</summary>
    public static IReadOnlyList<int> SleepMinutes { get; } = [15, 30, 45, 60, 90, 120];

    private DateTimeOffset? _sleepAt;

    /// <summary>What happens when the current item ends; once it has, back to <see cref="AfterItem.Continue"/>.</summary>
    public AfterItem AfterCurrent { get; private set; }

    /// <summary>Whether the sleep timer acts when the current item ends rather than at a time.</summary>
    public bool SleepAtEndOfItem { get; private set; }

    /// <summary>How long until the sleep timer acts, or null when it is off or waits for the item's end.</summary>
    public TimeSpan? SleepRemaining => _sleepAt is { } at ? Max(at - _time.GetUtcNow(), TimeSpan.Zero) : null;

    /// <summary>How loud the sleep timer's fade lets the sound be: 1 until its last seconds.</summary>
    internal double FadeLevel => _fade;

    /// <summary>Whether the sleep timer is set, either way.</summary>
    public bool SleepTimerSet => _sleepAt is not null || SleepAtEndOfItem;

    /// <summary>Sets what happens at the end of the current item, or turns that off when it is already set so.</summary>
    public void ToggleAfterCurrent(AfterItem after)
    {
        AfterCurrent = AfterCurrent == after ? AfterItem.Continue : after;
        Say(AfterCurrent switch
        {
            AfterItem.Stop => "Stop after this item",
            AfterItem.Pause => "Pause after this item",
            _ => "Play on after this item",
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Pauses or stops (as the settings say) <paramref name="after"/> from now, the sound fading
    /// over its last seconds; null turns the timer off (PB-21).
    /// </summary>
    public void SetSleepTimer(TimeSpan? after)
    {
        SleepAtEndOfItem = false;
        var wait = after is { } asked ? Max(asked, TimeSpan.Zero) : (TimeSpan?)null;
        _sleepAt = wait is { } span ? _time.GetUtcNow() + span : null;
        ApplyFade(1);
        var minutes = (int)Math.Round(wait?.TotalMinutes ?? 0);
        Say(wait is null ? "Sleep timer off" : minutes == 1 ? "Sleep timer: 1 minute" : $"Sleep timer: {minutes} minutes");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pauses or stops when the current item ends, fading over its last seconds.</summary>
    public void SetSleepAtEndOfItem()
    {
        _sleepAt = null;
        SleepAtEndOfItem = true;
        Say("Sleep timer: at the end of this item");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves the sleep timer on; the window calls this about once a second. In the last seconds the
    /// sound fades; at the time, playing pauses or stops and the volume comes back for next time.
    /// </summary>
    public void SleepTick()
    {
        TimeSpan? left = _sleepAt is not null ? SleepRemaining
            : SleepAtEndOfItem && Duration > TimeSpan.Zero && _session is not null ? Max(Duration - Position, TimeSpan.Zero)
            : null;
        if (left is not { } remaining)
        {
            return;
        }

        if (_sleepAt is not null && remaining <= TimeSpan.Zero)
        {
            _sleepAt = null;
            ApplyFade(1);
            if (Settings.SleepAction == SleepChoice.Stop)
            {
                Stop();
            }
            else if (IsPlaying)
            {
                PlayPause();
            }

            Say("Sleep timer: good night");
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        ApplyFade(remaining < SleepFade ? remaining / SleepFade : 1);
    }

    /// <summary>The commands for what happens after this item and for the sleep timer; false for any other.</summary>
    private bool ExecuteTimers(string commandId)
    {
        switch (commandId)
        {
            case CommandCatalog.StopAfterCurrent:
                ToggleAfterCurrent(AfterItem.Stop);
                return true;
            case CommandCatalog.PauseAfterCurrent:
                ToggleAfterCurrent(AfterItem.Pause);
                return true;
            case CommandCatalog.SleepOff:
                SetSleepTimer(null);
                return true;
            case CommandCatalog.SleepAtEndOfItem:
                SetSleepAtEndOfItem();
                return true;
            default:
                if (CommandCatalog.SleepMinutesOf(commandId) is { } minutes)
                {
                    SetSleepTimer(TimeSpan.FromMinutes(minutes));
                    return true;
                }

                return false;
        }
    }

    /// <summary>
    /// The current item is over: what the user asked for at its end (stop, or the next item
    /// paused), or the sleep timer if it waited for this; otherwise <paramref name="playOn"/>.
    /// </summary>
    private void FinishItem(Action playOn)
    {
        switch (TakeAfter())
        {
            case AfterItem.Stop:
                Stop();
                break;
            case AfterItem.Pause:
                if (Playlist.Next(automatic: true) is { } next)
                {
                    Start(next, paused: true);
                }
                else
                {
                    EndSession();
                    State = Rex.Media.Engine.SessionState.Ended;
                    Changed?.Invoke(this, EventArgs.Empty);
                }

                break;
            default:
                playOn();
                break;
        }
    }

    /// <summary>What was asked for at the end of this item, which is then done with: by the user, or by a sleep timer waiting for it.</summary>
    private AfterItem TakeAfter()
    {
        var after = AfterCurrent;
        AfterCurrent = AfterItem.Continue;
        if (SleepAtEndOfItem)
        {
            SleepAtEndOfItem = false;
            ApplyFade(1);
            after = Settings.SleepAction == SleepChoice.Stop ? AfterItem.Stop : AfterItem.Pause;
        }

        return after;
    }

    /// <summary>Whether the end of this item must wait for <see cref="FinishItem"/> rather than run on into the next.</summary>
    private bool HoldsAtTheEnd => AfterCurrent != AfterItem.Continue || SleepAtEndOfItem;

    private void ApplyFade(double share)
    {
        _fade = Math.Clamp(share, 0, 1);
        if (_session is not null)
        {
            _session.Volume = Volume * _fade;
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
