using Rex.Media.AppCore.Design;

namespace Rex.Media.App.Mac;

/// <summary>Reusable nonmodal confirmation banner: a decision does not block playback or the run loop.</summary>
public sealed partial class MainWindow
{
    private NSView? _confirmationBanner;
    private RexLabel? _confirmationText;
    private Action? _confirmationAccept;
    private NSView? _entryBanner;
    private RexLabel? _entryPrompt;
    private NSTextField? _entryField;
    private Action<string>? _entryAccepted;

    /// <summary>
    /// A banner above the seek row: a raised, rounded row holding <paramref name="views"/>, hidden
    /// until it has something to ask. Every banner in the bar is drawn the same way.
    /// </summary>
    private static RexPanel Banner(bool vertical, params NSView[] views)
    {
        var panel = MacTheme.Panel(DesignRole.Raised, 1);
        var row = MacTheme.Stack(vertical, 1, views);
        MacTheme.Fill(panel, row, (double)MacTheme.Space(1));
        panel.Hidden = true;
        return panel;
    }

    /// <summary>The in-window question and the in-window text prompt, for the bar to stack above its seek row.</summary>
    private (NSView Confirmation, NSView Entry) BuildConfirmationBanners()
    {
        _confirmationText = MacTheme.WrappingLabel("");
        _confirmationText.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        var accept = MacTheme.TextButton("Continue", () =>
        {
            var action = _confirmationAccept;
            HideConfirmation();
            action?.Invoke();
        }, RexButtonLook.Accent);
        var cancel = MacTheme.TextButton("Cancel", HideConfirmation);
        _confirmationText.WidthAnchor.ConstraintGreaterThanOrEqualTo(230).Active = true;
        _confirmationBanner = Banner(false, _confirmationText, accept, cancel);

        _entryPrompt = MacTheme.WrappingLabel("");
        _entryField = new NSTextField(new CGRect(0, 0, 340, 24)) { TranslatesAutoresizingMaskIntoConstraints = false };
        ((NSView)_entryField).AccessibilityLabel = "Command input";
        _entryField.WidthAnchor.ConstraintGreaterThanOrEqualTo(220).Active = true;
        var submit = MacTheme.TextButton("Apply", SubmitEntryPrompt, RexButtonLook.Accent);
        submit.KeyEquivalent = "\r";
        var dismiss = MacTheme.TextButton("Cancel", HideEntryPrompt);
        var actions = MacTheme.Stack(false, 1, submit, dismiss);
        _entryBanner = Banner(true, _entryPrompt, _entryField, actions);
        return (_confirmationBanner, _entryBanner);
    }

    private void ConfirmInWindow(string question, Action accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        _confirmationAccept = accept;
        if (_confirmationText is null || _confirmationBanner is null)
        {
            HideConfirmation();
            return;
        }

        _confirmationText.StringValue = question;
        RevealMacChromeForPrompt();
        _confirmationBanner.Hidden = false;
        _window.MakeKeyAndOrderFront(null);
    }

    private void RequestEntryInWindow(string prompt, string initial, string placeholder,
        Action<string> accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        if (_entryBanner is null || _entryPrompt is null || _entryField is null) return;
        HideConfirmation();
        _entryAccepted = accept;
        _entryPrompt.StringValue = prompt;
        _entryField.StringValue = initial;
        _entryField.PlaceholderString = placeholder;
        RevealMacChromeForPrompt();
        _entryBanner.Hidden = false;
        _window.MakeKeyAndOrderFront(null);
        _window.MakeFirstResponder(_entryField);
        _entryField.SelectText(null);
    }

    private void RevealMacChromeForPrompt()
    {
        // Prompts must remain visible even when a video uses minimal controls.
        if (_minimalInterface)
        {
            _minimalInterface = false;
            PlaceChrome();
        }

        RevealFullScreenController();
    }

    private void SubmitEntryPrompt()
    {
        var accept = _entryAccepted;
        var value = _entryField?.StringValue ?? "";
        HideEntryPrompt();
        accept?.Invoke(value);
    }

    private void HideEntryPrompt()
    {
        _entryAccepted = null;
        if (_entryBanner is not null) _entryBanner.Hidden = true;
    }

    private void HideConfirmation()
    {
        _confirmationAccept = null;
        if (_confirmationBanner is not null)
        {
            _confirmationBanner.Hidden = true;
        }
    }
}
