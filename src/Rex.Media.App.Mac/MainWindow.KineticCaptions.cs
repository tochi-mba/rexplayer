using Rex.Media.AppCore.Captions;
using Rex.Media.AppCore.Automation;
using Rex.Media.AppCore.Design;
using Rex.Media.AppCore.Player;
using Rex.Media.Primitives;
using Rex.Media.Settings;
using Rex.Media.Subtitles;
using System.Globalization;

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
    private RexLabel? _macCaptionEnergyValue;
    private RexLabel? _macCaptionAbout;
    private NSButton? _macCaptionSubtitles;
    private NSPopUpButton? _macCaptionSource;
    private NSPopUpButton? _macCaptionLayout;
    private NSPopUpButton? _macCaptionPalette;
    private NSButton? _macCaptionGlyphs;
    private NSTextField? _macCaptionAccent;
    private SubtitleTrack? _macCaptionTimedTrack;
    private SubtitleTrack? _macCaptionTimedFrom;
    private Transcript? _macCaptionTimedBy;
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
        _macCaptionTimedTrack = null;
        _macCaptionTimedFrom = null;
        _macCaptionTimedBy = null;
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
            elapsed, MacKineticCues(_macCaptionPlayhead), area, size);
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

    private IReadOnlyList<SubtitleCue> MacKineticCues(TimeSpan at)
    {
        var subtitles = _player.SubtitlesAt(at);
        var transcript = _macTrack?.At(at) ?? [];
        return _settings.KineticCaptions.Words switch
        {
            CaptionWordSource.Subtitles => subtitles,
            CaptionWordSource.Transcript => transcript,
            CaptionWordSource.SubtitlesTimedByTranscript => TimedMacCaptionCues(at, subtitles),
            _ => subtitles.Count > 0 ? subtitles : transcript,
        };
    }

    private IReadOnlyList<SubtitleCue> TimedMacCaptionCues(TimeSpan at,
        IReadOnlyList<SubtitleCue> fallback)
    {
        if (_player.Subtitles is not { } source || _macTranscript is not { } spoken)
            return fallback;
        if (!ReferenceEquals(source, _macCaptionTimedFrom) ||
            !ReferenceEquals(spoken, _macCaptionTimedBy))
        {
            _macCaptionTimedFrom = source;
            _macCaptionTimedBy = spoken;
            _macCaptionTimedTrack = TranscriptAlignment.Time(source, spoken);
        }

        return _macCaptionTimedTrack?.At(at) ?? fallback;
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
            _macCaptionSettings = MacToolViews.ToolWindow("Kinetic caption settings - rexplayer", 620, 640, 540, 420);
            _macCaptionPreset = EffectChoices([.. Enum.GetValues<CaptionPreset>().Select(CaptionPresets.Name)]);
            _macCaptionPreset.Activated += (_, _) => ApplyMacCaptionSettings();
            _macCaptionAbout = MacTheme.WrappingLabel("", 1, DesignRole.TextSecondary);
            _macCaptionEnergy = NSSlider.FromValue(100, KineticCaptionSettings.LeastEnergy,
                KineticCaptionSettings.MostEnergy, ApplyMacCaptionSettings);
            _macCaptionEnergy.WidthAnchor.ConstraintGreaterThanOrEqualTo(180).Active = true;
            ((NSView)_macCaptionEnergy).AccessibilityLabel = "Energy";
            _macCaptionEnergyValue = MacTheme.Label("", 1, DesignRole.TextSecondary);
            _macCaptionEnergyValue.Font = MacTheme.DigitFont(1);
            _macCaptionEnergyValue.Alignment = NSTextAlignment.Right;
            _macCaptionEnergyValue.WidthAnchor.ConstraintEqualTo(44).Active = true;
            _macCaptionEnergy.Activated += (_, _) => ShowMacCaptionEnergy();
            _macCaptionSubtitles = NSButton.CreateCheckbox(
                "Keep the ordinary subtitles as well", ApplyMacCaptionSettings);
            _macCaptionSource = EffectChoices([.. Enum.GetValues<CaptionWordSource>().Select(CaptionCommands.WordsName)]);
            _macCaptionLayout = EffectChoices([.. Enum.GetValues<CaptionLayoutChoice>().Select(CaptionCommands.LayoutName)]);
            _macCaptionPalette = EffectChoices(Enum.GetNames<CaptionColors>());
            foreach (var control in new[] { _macCaptionSource, _macCaptionLayout, _macCaptionPalette })
                control.Activated += (_, _) => ApplyMacCaptionSettings();
            _macCaptionGlyphs = NSButton.CreateCheckbox("Small pictures beside some words", ApplyMacCaptionSettings);
            _macCaptionAccent = new NSTextField(new CGRect(0, 0, 120, 24))
            {
                PlaceholderString = "FFB400",
            };
            _macCaptionAccent.WidthAnchor.ConstraintEqualTo(100).Active = true;
            _macCaptionAccent.Activated += (_, _) => ApplyMacCaptionSettings();
            ((NSView)_macCaptionAccent).AccessibilityLabel = "Kinetic captions accent colour in hex";
            var toggle = MacTheme.TextButton("Toggle captions", ToggleMacKinetic, RexButtonLook.Accent);
            var page = MacToolViews.ToolPage("Kinetic captions",
                "Source-timed words arrive as speech is heard; movement and accent are " +
                "computed by the shared caption director from local audio levels and tempo.",
                MacToolViews.ToolCard("Captions",
                    MacToolViews.ToolRow("Look", null, _macCaptionPreset),
                    _macCaptionAbout,
                    MacToolViews.ToolRow("Energy (%)", "How far the voice moves the words",
                        MacToolViews.ToolButtons(_macCaptionEnergy, _macCaptionEnergyValue)),
                    MacToolViews.ToolRow("Words from", null, _macCaptionSource),
                    MacToolViews.ToolCheck(_macCaptionGlyphs),
                    MacToolViews.ToolCheck(_macCaptionSubtitles)),
                MacToolViews.ToolCard("Custom look",
                    MacToolViews.ToolRow("Layout", null, _macCaptionLayout),
                    MacToolViews.ToolRow("Colours for loud words", null, _macCaptionPalette),
                    MacToolViews.ToolRow("Accent colour", "#RRGGBB", _macCaptionAccent)),
                MacToolViews.ToolButtons(toggle));
            MacToolViews.Host(_macCaptionSettings, page, scrolls: true);
            _macCaptionSettings.Center();
        }

        _macCaptionPreset!.SelectItem((int)_settings.KineticCaptions.Preset);
        _macCaptionEnergy!.DoubleValue = _settings.KineticCaptions.Energy;
        _macCaptionSubtitles!.State = State(_settings.KineticCaptions.KeepSubtitles);
        _macCaptionSource!.SelectItem((int)_settings.KineticCaptions.Words);
        _macCaptionLayout!.SelectItem((int)_settings.KineticCaptions.Layout);
        _macCaptionPalette!.SelectItem((int)_settings.KineticCaptions.Colors);
        _macCaptionGlyphs!.State = State(_settings.KineticCaptions.Glyphs);
        _macCaptionAccent!.StringValue = _settings.KineticCaptions.Accent.ToString("X6", CultureInfo.InvariantCulture);
        ShowMacCaptionEnergy();
        _macCaptionSettings.MakeKeyAndOrderFront(null);
    }

    /// <summary>The energy in figures beside its slider, and the chosen look described under its picker.</summary>
    private void ShowMacCaptionEnergy()
    {
        if (_macCaptionEnergyValue is not null && _macCaptionEnergy is not null)
        {
            _macCaptionEnergyValue.StringValue = Math.Round(_macCaptionEnergy.DoubleValue).ToString("0", CultureInfo.InvariantCulture);
        }

        if (_macCaptionAbout is not null)
        {
            _macCaptionAbout.StringValue = CaptionPresets.Describe(_settings.KineticCaptions.Preset);
        }
    }

    private void ApplyMacCaptionSettings()
    {
        if (_macCaptionPreset is null || _macCaptionEnergy is null ||
            _macCaptionSubtitles is null || _macCaptionSource is null ||
            _macCaptionLayout is null || _macCaptionPalette is null ||
            _macCaptionGlyphs is null || _macCaptionAccent is null) return;
        var hex = _macCaptionAccent.StringValue.Trim().TrimStart('#');
        var accent = hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : _settings.KineticCaptions.Accent;
        SaveMacCaptionSettings(_settings with
        {
            KineticCaptions = _settings.KineticCaptions with
            {
                Preset = (CaptionPreset)(int)_macCaptionPreset.IndexOfSelectedItem,
                Energy = (int)Math.Round(_macCaptionEnergy.DoubleValue),
                KeepSubtitles = IsChecked(_macCaptionSubtitles),
                Words = (CaptionWordSource)(int)_macCaptionSource.IndexOfSelectedItem,
                Layout = (CaptionLayoutChoice)(int)_macCaptionLayout.IndexOfSelectedItem,
                Colors = (CaptionColors)(int)_macCaptionPalette.IndexOfSelectedItem,
                Accent = accent,
                Glyphs = IsChecked(_macCaptionGlyphs),
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
            ShowMacCaptionEnergy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("Could not save kinetic caption preferences: " + ex.Message);
        }
    }

    private AutomationReply MacKineticSmokeAction(string? action)
    {
        if (action == "configure")
        {
            SaveMacCaptionSettings(_settings with
            {
                KineticCaptions = _settings.KineticCaptions with
                {
                    On = true,
                    Preset = CaptionPreset.Custom,
                    Energy = 175,
                    Words = CaptionWordSource.SubtitlesTimedByTranscript,
                    Layout = CaptionLayoutChoice.Stairs,
                    Colors = CaptionColors.Ocean,
                    Accent = 0x12A4E0,
                    Glyphs = true,
                },
            });
        }
        else if (action != "status")
        {
            return new AutomationReply { Ok = false, Error = "Unknown kinetic-caption action." };
        }

        var captions = _settings.KineticCaptions;
        return new AutomationReply
        {
            Ok = true,
            Text = $"on={captions.On};preset={captions.Preset};energy={captions.Energy};" +
                $"words={captions.Words};layout={captions.Layout};palette={captions.Colors};" +
                $"accent={captions.Accent:X6};glyphs={captions.Glyphs}",
        };
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
