using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Rex.Media.Primitives;
using Rex.Media.Video.D3D11;

namespace Rex.Media.App;

/// <summary>Picture style menus and independent, live-preview preset controls.</summary>
public sealed partial class MainWindow
{
    private readonly List<(RadioMenuFlyoutItem Item, VideoLook Look)> _videoLookItems = [];

    /// <summary>One-tap, accessible GPU picture looks from the Video and picture context menus.</summary>
    private void AddPictureLooks(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var looks = new MenuFlyoutSubItem { Text = "Picture look" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(looks, prefix + "Menu");
        var groups = new Dictionary<string, MenuFlyoutSubItem>(StringComparer.Ordinal);
        foreach (var look in Enum.GetValues<VideoLook>())
        {
            var name = VideoLooks.Name(look);
            var item = new RadioMenuFlyoutItem
            {
                Text = name,
                GroupName = prefix + "PictureLook",
                IsChecked = look == _settings.VideoLook,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, prefix + look);
            item.Click += (_, _) => ChooseVideoLook(look);
            _videoLookItems.Add((item, look));
            var category = VideoLooks.Category(look);
            if (category == "Essentials")
            {
                looks.Items.Add(item);
                continue;
            }

            if (!groups.TryGetValue(category, out var group))
            {
                group = new MenuFlyoutSubItem { Text = category };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(group, prefix + "Group-" + category);
                groups.Add(category, group);
                looks.Items.Add(group);
            }

            group.Items.Add(item);
        }

        var customize = new MenuFlyoutItem { Text = "Customize current look..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(customize, prefix + "Customize");
        customize.Click += (_, _) => _ = ShowVideoStyleSettingsAsync(effect: false);
        looks.Items.Add(new MenuFlyoutSeparator());
        looks.Items.Add(customize);
        items.Add(new MenuFlyoutSeparator());
        items.Add(looks);
    }

    private readonly List<(RadioMenuFlyoutItem Item, VideoEffect Effect)> _videoEffectItems = [];

    /// <summary>Grouped real-time picture effects, independently selectable from colour looks.</summary>
    private void AddPictureEffects(IList<MenuFlyoutItemBase> items, string prefix)
    {
        var sub = new MenuFlyoutSubItem { Text = "Picture effects" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(sub, prefix + "Menu");
        var groups = new Dictionary<string, MenuFlyoutSubItem>(StringComparer.Ordinal);
        foreach (var effect in Enum.GetValues<VideoEffect>())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = VideoEffects.Name(effect),
                GroupName = prefix + "PictureEffects",
                IsChecked = effect == _settings.VideoEffect,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, prefix + effect);
            item.Click += (_, _) => ChooseVideoEffect(effect);
            _videoEffectItems.Add((item, effect));
            var category = VideoEffects.Category(effect);
            if (category == "Essentials")
            {
                sub.Items.Add(item);
            }
            else
            {
                if (!groups.TryGetValue(category, out var group))
                {
                    group = new MenuFlyoutSubItem { Text = category };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(group, prefix + "Group-" + category.Replace(" ", ""));
                    groups.Add(category, group);
                    sub.Items.Add(group);
                }

                group.Items.Add(item);
            }
        }

        var customize = new MenuFlyoutItem { Text = "Customize current effect..." };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(customize, prefix + "Customize");
        customize.Click += (_, _) => _ = ShowVideoStyleSettingsAsync(effect: true);
        sub.Items.Add(new MenuFlyoutSeparator());
        sub.Items.Add(customize);
        items.Add(sub);
    }

    private void ApplyVideoEffect(VideoEffect effect, int strength)
    {
        foreach (var (item, chosen) in _videoEffectItems)
        {
            item.IsChecked = chosen == effect;
        }

        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            // Interactive effects must not reuse a pointer left by a previous effect.
            // The next genuine pointer movement will provide a fresh coordinate.
            if (!VideoEffects.UsesPointer(effect))
            {
                presenter.SetPointer(0.5f, 0.5f, false);
            }

            ConfigureEffect(presenter, effect, _settings.VideoStyleValues, strength);
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    /// <summary>
    /// Hands the presenter all four of an effect's saved controls. The one place this happens, so
    /// starting the player, choosing an effect and changing its settings always agree.
    /// </summary>
    private void ConfigureEffect(D3D11Presenter presenter, VideoEffect effect, IReadOnlyDictionary<string, int> values, int legacyStrength)
    {
        var controls = VideoStyleOptions.Controls(effect, values, legacyStrength);

        // On a weak computer the heaviest effects stay off (DeviceProfile.HeavyEffects); the choice is kept.
        presenter.SetEffect(_budget.Allows(effect) ? effect : VideoEffect.Off, controls.Intensity, controls.Detail);
        presenter.SetEffectExtras(controls.Third, controls.Fourth);
    }

    private void ChooseVideoEffect(VideoEffect effect)
    {
        _settings = _settings with { VideoEffect = effect };
        RememberLater();
        ApplyVideoEffect(effect, _settings.VideoEffectStrength);
        Say(_budget.Allows(effect)
            ? "Picture effects: " + VideoEffects.Name(effect)
            : $"{VideoEffects.Name(effect)} is off on this computer to keep video smooth. Choose Best quality under Performance in Preferences to use it.");
    }

    /// <summary>Cycles the picture effect from the shared Z shortcut while pictures are showing.</summary>
    private void CycleVideoEffect() => ChooseVideoEffect(VideoEffects.Next(_settings.VideoEffect));

    /// <summary>
    /// Per-look controls preview immediately, but Cancel returns the renderer and settings to
    /// the prior values. Reset removes only this style's keys, never another style's presets.
    /// </summary>
    private async Task ShowVideoStyleSettingsAsync(bool effect)
    {
        var options = effect ? VideoStyleOptions.ForEffect(_settings.VideoEffect) : VideoStyleOptions.ForLook(_settings.VideoLook);
        var title = effect ? VideoEffects.Name(_settings.VideoEffect) : VideoLooks.Name(_settings.VideoLook);
        var before = _settings.VideoStyleValues;
        var content = new StackPanel { Spacing = 12, MinWidth = 300, MaxWidth = 460 };
        string explanation;
        if (effect && options.Count == 0)
        {
            explanation = "No picture effect is on. Choose one from Picture effects first; each keeps its own settings.";
        }
        else if (effect)
        {
            explanation = VideoEffects.Category(_settings.VideoEffect) + ". " + VideoEffects.Description(_settings.VideoEffect)
                + " Changes preview live; Cancel restores the previous settings. Snapshots save the original picture, without the effect.";
        }
        else
        {
            explanation = "Changes preview live. Each look remembers its own settings. Cancel restores your previous values.";
        }

        var description = new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(description, "VideoStyleDescription");
        content.Children.Add(description);
        var sliders = new List<(VideoStyleOption Option, Action<int> SetValue)>();
        var resetting = false;
        void Apply()
        {
            if (effect)
            {
                ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
            }
            else
            {
                ApplyVideoLook(_settings.VideoLook);
            }
        }

        int Initial(VideoStyleOption option) => effect
            ? VideoStyleOptions.ReadEffect(_settings.VideoEffect, before, option, _settings.VideoEffectStrength)
            : VideoStyleOptions.Read(before, option);

        // Every control says what it changes, on hover and to screen readers.
        static void Explain(FrameworkElement control, VideoStyleOption option)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(control, "VideoStyle-" + option.Key);
            if (option.Help.Length > 0)
            {
                ToolTipService.SetToolTip(control, option.Help);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(control, option.Help);
            }
        }

        foreach (var option in options)
        {
            void Preview(int value)
            {
                // Moving the controls back to their defaults must not write the keys Reset removed.
                if (resetting)
                {
                    return;
                }

                _settings = _settings with
                {
                    VideoStyleValues = VideoStyleOptions.With(_settings.VideoStyleValues, option, value),
                };
                Apply();
            }

            if (effect && option.Key.EndsWith(".mode", StringComparison.Ordinal))
            {
                // Modes are named, not a cryptic 0-2 numeric control.
                var mode = new ComboBox
                {
                    Header = option.Label,
                    ItemsSource = VideoStyleOptions.ModeNames(_settings.VideoEffect),
                    SelectedIndex = Initial(option),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mode, option.Label);
                Explain(mode, option);
                mode.SelectionChanged += (_, _) =>
                {
                    if (mode.SelectedIndex >= 0)
                    {
                        Preview(mode.SelectedIndex);
                    }
                };
                sliders.Add((option, value => mode.SelectedIndex = value));
                content.Children.Add(mode);
            }
            else
            {
                var box = Number(option.Label, Initial(option), option.Min, option.Max);
                Explain(box, option);
                box.ValueChanged += (_, _) =>
                {
                    if (double.IsFinite(box.Value))
                    {
                        Preview((int)box.Value);
                    }
                };
                sliders.Add((option, value => box.Value = value));
                content.Children.Add(box);
            }
        }

        var reset = new Button
        {
            Content = effect ? "Reset this effect to its defaults" : "Reset this look to its defaults",
            Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reset, "VideoStyleReset");
        ToolTipService.SetToolTip(reset, "Puts only this one's settings back; every other look and effect keeps its own.");
        reset.Click += (_, _) =>
        {
            _settings = _settings with { VideoStyleValues = VideoStyleOptions.Reset(_settings.VideoStyleValues, options) };
            resetting = true;
            try
            {
                foreach (var (option, setValue) in sliders)
                {
                    setValue(option.Default);
                }
            }
            finally
            {
                resetting = false;
            }

            Apply();
        };
        content.Children.Add(reset);

        if (!await Ask(title + " settings", content, "Save"))
        {
            _settings = _settings with { VideoStyleValues = before };
        }
        else
        {
            SaveSettings();
        }

        if (effect)
        {
            ApplyVideoEffect(_settings.VideoEffect, _settings.VideoEffectStrength);
        }
        else
        {
            ApplyVideoLook(_settings.VideoLook);
        }
    }

    /// <summary>Changes the renderer live and redraws the current picture when paused.</summary>
    private void ApplyVideoLook(VideoLook look)
    {
        foreach (var (item, selected) in _videoLookItems)
        {
            item.IsChecked = selected == look;
        }

        var redraw = !_player.IsPlaying;
        OnPresenterThread(presenter =>
        {
            var options = VideoStyleOptions.ForLook(look);
            var values = _settings.VideoStyleValues;
            presenter.SetLook(look, VideoStyleOptions.Read(values, options[0]), VideoStyleOptions.Read(values, options[1]));
            if (redraw)
            {
                presenter.Redraw();
            }
        });
    }

    private void ChooseVideoLook(VideoLook look)
    {
        _settings = _settings with { VideoLook = look };
        RememberLater();
        ApplyVideoLook(look);
        Say("Picture look: " + VideoLooks.Name(look));
    }

}
