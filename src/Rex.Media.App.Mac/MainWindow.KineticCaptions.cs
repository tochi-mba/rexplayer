using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Settings;

namespace Rex.Media.App.Mac;

/// <summary>Native caption-word views driven by the portable speech-reactive caption director.</summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<int, NSTextField> _macCaptionWords = [];
    private readonly float[] _macCaptionLeft = new float[SpeechPulse.Size];
    private readonly float[] _macCaptionRight = new float[SpeechPulse.Size];
    private readonly float[] _macCaptionMono = new float[SpeechPulse.Size];
    private CaptionDirector? _macCaptionDirector;
    private NSTimer? _macCaptionTimer;
    private NSWindow? _macCaptionSettings;
    private NSPopUpButton? _macCaptionPreset;
    private NSSlider? _macCaptionEnergy;
    private NSButton? _macCaptionSubtitles;
    private NSView? _macCaptionStage;
    private DateTimeOffset _macCaptionFrameAt;
    private TimeSpan _macCaptionPlayhead;
    private TimeSpan _macCaptionReported;

    private void AddMacKineticLayer(NSView stage)
    {
        _macCaptionStage = stage;
        _macCaptionTimer = NSTimer.CreateRepeatingScheduledTimer(
            TimeSpan.FromMilliseconds(33), _ => DrawMacKineticCaptions());
    }

    private bool MacKineticVisible => _settings.KineticCaptions.On &&
        _player.Item is not null &&
        _player.Info?.FirstTrack(MediaKind.Video) is not null;

    private void ResetMacCaptionDirector()
    {
        _macCaptionDirector = null;
        _macCaptionReported = TimeSpan.Zero;
        _macCaptionPlayhead = TimeSpan.Zero;
        _macCaptionFrameAt = default;
        ClearMacCaptionWords();
    }

    private void DrawMacKineticCaptions()
    {
        if (!MacKineticVisible || _picture is null || _macCaptionStage is null)
        {
            if (_macCaptionWords.Count > 0) ClearMacCaptionWords();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = _macCaptionFrameAt == default
            ? TimeSpan.FromMilliseconds(33) : now - _macCaptionFrameAt;
        _macCaptionFrameAt = now;
        elapsed = elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(1)
            ? TimeSpan.FromMilliseconds(33) : elapsed;
        var reported = _player.Position;
        if (reported != _macCaptionReported)
        {
            _macCaptionReported = reported;
            _macCaptionPlayhead = reported;
        }
        else if (_player.IsPlaying)
        {
            _macCaptionPlayhead += TimeSpan.FromTicks((long)(elapsed.Ticks * _player.Speed));
        }

        if (_macCaptionDirector is null)
        {
            _macCaptionDirector = new CaptionDirector(CaptionDirector.StyleFor(_settings));
        }

        var heard = _player.IsPlaying && _player.ReadSound(_macCaptionLeft, _macCaptionRight);
        if (heard)
        {
            for (var i = 0; i < _macCaptionMono.Length; i++)
            {
                _macCaptionMono[i] = (_macCaptionLeft[i] + _macCaptionRight[i]) * 0.5f;
            }
        }

        var area = new Area(0, 0, (double)_picture.Bounds.Width, (double)_picture.Bounds.Height);
        if (area.Width < 30 || area.Height < 30) return;
        var size = Math.Max(13, area.Height * 0.055 * _settings.SubtitleSize / 100.0);
        var words = _macCaptionDirector.Frame(_macCaptionPlayhead,
            heard ? _macCaptionMono : [], _player.SoundSampleRate,
            elapsed, _player.SubtitlesAt(_macCaptionPlayhead), area, size);
        var visible = new HashSet<int>();
        foreach (var word in words)
        {
            visible.Add(word.Id);
            if (!_macCaptionWords.TryGetValue(word.Id, out var label))
            {
                label = NSTextField.CreateLabel(word.Text);
                label.Alignment = NSTextAlignment.Center;
                label.TranslatesAutoresizingMaskIntoConstraints = true;
                _macCaptionStage.AddSubview(label);
                _macCaptionWords.Add(word.Id, label);
            }

            label.StringValue = word.Text;
            var fontSize = (nfloat)Math.Clamp(size * word.Size, 12, 160);
            label.Font = NSFont.SystemFontOfSize(fontSize,
                word.Bold ? NSFontWeight.Bold : NSFontWeight.Medium)!;
            var color = word.Color;
            label.TextColor = NSColor.FromRgba(color.R, color.G, color.B,
                (byte)Math.Clamp(Math.Round(color.A * word.Opacity), 0, 255));
            var width = Math.Clamp(word.Text.Length * (double)fontSize * 0.72 + 18, 22, area.Width);
            var height = Math.Max(24, (double)fontSize * 1.6);
            var x = Math.Clamp((double)_picture.Frame.X + word.X + word.OffsetX * size - width / 2,
                (double)_picture.Frame.X, (double)_picture.Frame.X + area.Width - width);
            var y = Math.Clamp((double)_picture.Frame.Y + area.Height -
                word.Y - word.OffsetY * size - height / 2,
                (double)_picture.Frame.Y, (double)_picture.Frame.Y + area.Height - height);
            label.Frame = new CGRect((nfloat)x, (nfloat)y, (nfloat)width, (nfloat)height);
            label.Hidden = false;
        }

        foreach (var id in _macCaptionWords.Keys.Where(id => !visible.Contains(id)).ToArray())
        {
            _macCaptionWords[id].RemoveFromSuperview();
            _macCaptionWords[id].Dispose();
            _macCaptionWords.Remove(id);
        }
    }

    private void ClearMacCaptionWords()
    {
        foreach (var label in _macCaptionWords.Values)
        {
            label.RemoveFromSuperview();
            label.Dispose();
        }

        _macCaptionWords.Clear();
    }

    private void ToggleMacKinetic()
    {
        SaveMacCaptionSettings(_settings with
        {
            KineticCaptions = _settings.KineticCaptions with { On = !_settings.KineticCaptions.On },
        });
    }

    private static T NextCaptionChoice<T>(T current) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, current);
        return values[(index + 1) % values.Length];
    }

    private void ChangeMacCaptionEnergy(int delta)
    {
        SaveMacCaptionSettings(_settings with
        {
            KineticCaptions = _settings.KineticCaptions with
            {
                Energy = Math.Clamp(_settings.KineticCaptions.Energy + delta,
                    KineticCaptionSettings.LeastEnergy, KineticCaptionSettings.MostEnergy),
            },
        });
    }

    private void CycleMacCaptionLayout() => SaveMacCaptionSettings(_settings with
    {
        KineticCaptions = _settings.KineticCaptions with
        {
            Layout = NextCaptionChoice(_settings.KineticCaptions.Layout),
        },
    });

    private void CycleMacCaptionSource() => SaveMacCaptionSettings(_settings with
    {
        KineticCaptions = _settings.KineticCaptions with
        {
            Words = NextCaptionChoice(_settings.KineticCaptions.Words),
        },
    });

    private void CycleMacCaptionPreset() => SaveMacCaptionSettings(_settings with
    {
        KineticCaptions = _settings.KineticCaptions with
        {
            Preset = NextCaptionChoice(_settings.KineticCaptions.Preset),
        },
    });

    private void ShowMacKineticSettings()
    {
        if (_macCaptionSettings is null)
        {
            _macCaptionSettings = new NSWindow(new CGRect(0, 0, 560, 320),
                NSWindowStyle.Titled | NSWindowStyle.Closable,
                NSBackingStore.Buffered, false)
            {
                Title = "Kinetic caption settings - rexplayer",
            };
            _macCaptionSettings.ReleaseWhenClosed(false);
            var title = NSTextField.CreateLabel("Kinetic captions");
            title.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold)!;
            var hint = NSTextField.CreateWrappingLabel(
                "Source-timed words arrive as speech is heard; movement and accent are " +
                "computed by the shared caption director from local audio levels and tempo.");
            hint.TextColor = NSColor.SecondaryLabel;
            _macCaptionPreset = EffectChoices(Enum.GetNames<CaptionPreset>());
            _macCaptionPreset.Activated += (_, _) => ApplyMacCaptionSettings();
            _macCaptionEnergy = NSSlider.FromValue(100, KineticCaptionSettings.LeastEnergy,
                KineticCaptionSettings.MostEnergy, ApplyMacCaptionSettings);
            _macCaptionSubtitles = NSButton.CreateCheckbox(
                "Keep ordinary subtitles below kinetic words", ApplyMacCaptionSettings);
            var toggle = NSButton.CreateButton("Toggle captions", ToggleMacKinetic);
            var stack = NSStackView.FromViews([
                title, hint, EffectChoiceRow("Style preset", _macCaptionPreset),
                StyleSliderRow("Energy", _macCaptionEnergy), _macCaptionSubtitles, toggle,
            ]);
            stack.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
            stack.Alignment = NSLayoutAttribute.Leading;
            stack.Spacing = 15;
            stack.EdgeInsets = new NSEdgeInsets(20, 24, 20, 24);
            stack.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView(new CGRect(0, 0, 560, 320));
            root.AddSubview(stack);
            stack.TopAnchor.ConstraintEqualTo(root.TopAnchor).Active = true;
            stack.BottomAnchor.ConstraintEqualTo(root.BottomAnchor).Active = true;
            stack.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor).Active = true;
            stack.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor).Active = true;
            hint.WidthAnchor.ConstraintEqualTo(stack.WidthAnchor, 1, -48).Active = true;
            _macCaptionSettings.ContentView = root;
            _macCaptionSettings.Center();
        }

        _macCaptionPreset!.SelectItem((int)_settings.KineticCaptions.Preset);
        _macCaptionEnergy!.DoubleValue = _settings.KineticCaptions.Energy;
        _macCaptionSubtitles!.State = State(_settings.KineticCaptions.KeepSubtitles);
        _macCaptionSettings.MakeKeyAndOrderFront(null);
    }

    private void ApplyMacCaptionSettings()
    {
        if (_macCaptionPreset is null || _macCaptionEnergy is null ||
            _macCaptionSubtitles is null) return;
        SaveMacCaptionSettings(_settings with
        {
            KineticCaptions = _settings.KineticCaptions with
            {
                Preset = (CaptionPreset)(int)_macCaptionPreset.IndexOfSelectedItem,
                Energy = (int)Math.Round(_macCaptionEnergy.DoubleValue),
                KeepSubtitles = IsChecked(_macCaptionSubtitles),
            },
        });
    }

    private void SaveMacCaptionSettings(PlayerSettings settings)
    {
        try
        {
            settings = settings.Normalize();
            SettingsStore.Save(_roots.SettingsPath, settings);
            _settings = settings;
            _player.Settings = settings;
            ResetMacCaptionDirector();
            UpdateSubtitleOverlays();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("Could not save kinetic caption preferences: " + ex.Message);
        }
    }

    private void DisposeMacKinetic()
    {
        _macCaptionTimer?.Invalidate();
        _macCaptionTimer = null;
        ClearMacCaptionWords();
        _macCaptionSettings?.Close();
        _macCaptionSettings?.Dispose();
        _macCaptionSettings = null;
    }
}
