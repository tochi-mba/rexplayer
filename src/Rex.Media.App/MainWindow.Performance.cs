using Microsoft.UI.Xaml.Media.Animation;
using Rex.Media.AppCore.Performance;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.App;

/// <summary>
/// Fitting the player to the computer (Preferences, Performance): the facts about it are read
/// at once where they are cheap (processor threads, memory, power), then completed once the
/// picture surface exists (what draws the pictures) and Windows has listed its decoders (which
/// codecs the graphics card decodes). <see cref="DeviceProfile"/> turns them and the mode in
/// effect into a <see cref="DeviceBudget"/>, applied here to the visualisations, the picture
/// effects, Subject Lock, the timeline preview, the lists' animations and the decode queues. A
/// change of power (energy saver, the charger) is applied as it happens, and while energy saver
/// is on <see cref="EnergySaverMode"/> may put Automatic in place of the chosen mode.
/// </summary>
public sealed partial class MainWindow
{
    private DeviceFacts _facts = QuickFacts();
    private DeviceBudget _budget = new();
    private EnergySaverMode _energy = EnergySaverMode.Start(PerformanceChoice.Automatic, true);
    private readonly Dictionary<Microsoft.UI.Xaml.Controls.ListViewBase, TransitionCollection> _listTransitions = [];

    /// <summary>The facts available at once: threads and memory this process may use, power, and the switches that imitate a weak computer.</summary>
    internal static DeviceFacts QuickFacts() => new()
    {
        Processors = Environment.ProcessorCount,
        MemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        Graphics = App.SoftwareGraphics ? GraphicsKind.Software : GraphicsKind.Unknown,
        HardwareDecoding = App.GraphicsCardDecoding ? null : [],
        OnBattery = App.Power.OnBattery,
        EnergySaver = App.Power.EnergySaver,
        SystemAnimations = AnimationsOn(),
    };

    private static bool AnimationsOn()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return true;
        }
    }

    /// <summary>
    /// The budget for the quick facts, applied before anything is shown, and the power watched from
    /// now on. The energy saver status already in the facts decides the mode in effect, so the
    /// window reads it once; a notice says so when it put Automatic in place of the chosen mode.
    /// </summary>
    private void WirePerformance()
    {
        var (energy, notice) = EnergySaverMode.Start(_settings.Performance, _settings.AutomaticWhenEnergySaver).PowerChanged(_facts.EnergySaver);
        _energy = energy;
        ApplyBudget(force: true);
        App.Power.Changed += OnPowerChanged;
        if (notice is not null)
        {
            Say(notice);
        }
    }

    private void UnwirePerformance() => App.Power.Changed -= OnPowerChanged;

    private void OnPowerChanged(object? sender, EventArgs e) => OnWindowThread(() =>
    {
        _facts = _facts with { OnBattery = App.Power.OnBattery, EnergySaver = App.Power.EnergySaver };
        var (energy, notice) = _energy.PowerChanged(_facts.EnergySaver);
        _energy = energy;
        ApplyBudget();
        if (notice is not null)
        {
            Say(notice);
        }
    });

    /// <summary>Preferences saved: the chosen mode and whether energy saver may replace it.</summary>
    private bool PerformanceChosen()
    {
        var before = _energy.Effective;
        _energy = _energy.WithChoice(_settings.Performance, _settings.AutomaticWhenEnergySaver);
        return _energy.Effective != before;
    }

    /// <summary>Completes the facts once the picture surface exists: what draws the pictures, and the codecs the graphics card decodes.</summary>
    private async Task CompleteFactsAsync()
    {
        if (_presenter is not { } presenter)
        {
            return;
        }

        var (adapter, decoding) = await Task.Run(() =>
        {
            var (software, vendor, memory) = presenter.Adapter;
            var graphics = DeviceProfile.GraphicsFrom(software, vendor, memory);
            IReadOnlyCollection<CodecId> codecs = App.GraphicsCardDecoding && graphics != GraphicsKind.Software
                ? [.. MfDecoderFactory.Survey().Where(entry => entry.Hardware.Count > 0).Select(entry => entry.Codec)]
                : [];
            return (graphics, codecs);
        });
        if (_closed)
        {
            return;
        }

        _facts = _facts with { Graphics = adapter, HardwareDecoding = decoding, MemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes };
        ApplyBudget();
    }

    /// <summary>Works out the budget for the facts and the mode in effect, and applies whatever changed.</summary>
    private void ApplyBudget(bool force = false)
    {
        var budget = DeviceProfile.For(DeviceProfile.Choose(_energy.Effective, _facts), _facts);
        if (!_settings.SeekPreview || !budget.SeekPreview)
        {
            ResetSeekPreview();
        }

        if (!force && budget == _budget)
        {
            return;
        }

        var before = _budget;
        _budget = budget;
        if (before.LibraryPictureSide != budget.LibraryPictureSide)
        {
            // Drop the old tier's decoded pictures; the on-disk originals remain
            // available to decode at the new width when rows next request them.
            _pictureMemory.Clear();
            _pictureOrder.Clear();
        }

        App.Log.Info(LogSource, $"Performance: {_energy.Describe(_facts)} {string.Join(" ", DeviceProfile.Changes(budget))}");
        OnPresenterThread(presenter => presenter.SubjectStep = budget.SubjectStep);
        if (!force && before.HeavyEffectsOff != budget.HeavyEffectsOff)
        {
            ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
        }

        foreach (var list in new Microsoft.UI.Xaml.Controls.ListViewBase[] { LibraryList, PlaylistView })
        {
            ShowListAnimations(list, budget.Animations);
        }

        // The navigation list changes its item template when the rail collapses or
        // temporarily expands. Entrance transitions can leave its re-created items
        // painted at an intermediate opacity, even when the rail has its final size.
        // Navigation is persistent, not a scrolling result list: show its items
        // immediately in every performance mode, including screenshot capture.
        ShowListAnimations(LibrarySources, false);

        if (_visualizing && before.VisualMaxSide != budget.VisualMaxSide)
        {
            BuildVisualizer();
        }

        // Changing a tier (including Automatic on power changes) applies to any open library
        // immediately, not only on the next playback event or transcription timer tick.
        UpdateBackgroundSectionPause();
        if (_transcriptionStarted)
        {
            TranscriptionTick();
        }
    }

    /// <summary>
    /// Lists animate items as they come and go; with many items on a weak computer that is work
    /// for nothing, so without animations they simply appear. The list's own animations come back
    /// when animations are on again.
    /// </summary>
    private void ShowListAnimations(Microsoft.UI.Xaml.Controls.ListViewBase list, bool on)
    {
        if (!on && !_listTransitions.ContainsKey(list))
        {
            _listTransitions[list] = list.ItemContainerTransitions;
            list.ItemContainerTransitions = [];
        }
        else if (on && _listTransitions.Remove(list, out var transitions))
        {
            list.ItemContainerTransitions = transitions;
        }
    }
}
