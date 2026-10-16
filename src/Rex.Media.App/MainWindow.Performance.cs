using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.System.Power;
using Rex.Media.AppCore.Performance;
using Rex.Media.Codecs.MediaFoundation;
using Rex.Media.Primitives;

namespace Rex.Media.App;

/// <summary>
/// Fitting the player to the computer (Preferences, Performance): the facts about it are read
/// at once where they are cheap (processor threads, memory, power), then completed once the
/// picture surface exists (what draws the pictures) and Windows has listed its decoders (which
/// codecs the graphics card decodes). <see cref="DeviceProfile"/> turns them and the user's choice
/// into a <see cref="DeviceBudget"/>, applied here to the visualisations, the picture effects,
/// Subject Lock, the timeline preview, the lists' animations and the
/// decode queues. A change of power (energy saver, the charger) is applied as it happens.
/// </summary>
public sealed partial class MainWindow
{
    private DeviceFacts _facts = QuickFacts();
    private DeviceBudget _budget = new();
    private readonly Dictionary<Microsoft.UI.Xaml.Controls.ListViewBase, TransitionCollection> _listTransitions = [];
    private bool _powerWatched;

    /// <summary>The facts available at once: threads and memory this process may use, power, and the switches that imitate a weak computer.</summary>
    internal static DeviceFacts QuickFacts() => new()
    {
        Processors = Environment.ProcessorCount,
        MemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        Graphics = App.SoftwareGraphics ? GraphicsKind.Software : GraphicsKind.Unknown,
        HardwareDecoding = App.GraphicsCardDecoding ? null : [],
        OnBattery = OnBattery(),
        EnergySaver = EnergySaver(),
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

    /// <summary>The budget for the quick facts, applied before anything is shown, and the power watched from now on.</summary>
    private void WirePerformance()
    {
        ApplyBudget(force: true);
        try
        {
            PowerManager.EnergySaverStatusChanged += OnPowerChanged;
            PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
            _powerWatched = true;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            App.Log.Debug(LogSource, "The power cannot be watched: " + ex.Message);
        }
    }

    private void UnwirePerformance()
    {
        if (_powerWatched)
        {
            PowerManager.EnergySaverStatusChanged -= OnPowerChanged;
            PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
            _powerWatched = false;
        }
    }

    private void OnPowerChanged(object? sender, object e) => DispatcherQueue.TryEnqueue(() =>
    {
        _facts = _facts with { OnBattery = OnBattery(), EnergySaver = EnergySaver() };
        ApplyBudget();
    });

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

    /// <summary>Works out the budget for the facts and the user's choice, and applies whatever changed.</summary>
    private void ApplyBudget(bool force = false)
    {
        var budget = DeviceProfile.For(DeviceProfile.Choose(_settings.Performance, _facts), _facts);
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

        App.Log.Info(LogSource, $"Performance: {DeviceProfile.Describe(_settings.Performance, _facts)} {string.Join(" ", DeviceProfile.Changes(budget))}");
        OnPresenterThread(presenter => presenter.SubjectStep = budget.SubjectStep);
        if (before.LargestSpeechModel != budget.LargestSpeechModel)
        {
            // The model transcription uses may have changed: whether it is installed is asked again.
            _modelInstalled = null;
        }

        if (!force && before.HeavyEffectsOff != budget.HeavyEffectsOff)
        {
            ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
        }

        foreach (var list in new Microsoft.UI.Xaml.Controls.ListViewBase[] { LibraryList, PlaylistView, LibrarySources })
        {
            ShowListAnimations(list, budget.Animations);
        }

        if (_visualizing && before.VisualMaxSide != budget.VisualMaxSide)
        {
            BuildVisualizer();
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
