namespace Rex.Media.App.Mac;

/// <summary>Reusable nonmodal confirmation banner: a decision does not block playback or the run loop.</summary>
public sealed partial class MainWindow
{
    private NSStackView? _confirmationBanner;
    private NSTextField? _confirmationText;
    private Action? _confirmationAccept;
    private NSStackView? _entryBanner;
    private NSTextField? _entryPrompt;
    private NSTextField? _entryField;
    private Action<string>? _entryAccepted;

    private void AddConfirmationBanner(NSStackView chrome)
    {
        _confirmationText = NSTextField.CreateWrappingLabel("");
        _confirmationText.TextColor = NSColor.SecondaryLabel;
        var accept = NSButton.CreateButton("Continue", () =>
        {
            var action = _confirmationAccept;
            HideConfirmation();
            action?.Invoke();
        });
        var cancel = NSButton.CreateButton("Cancel", HideConfirmation);
        _confirmationBanner = NSStackView.FromViews([_confirmationText, accept, cancel]);
        _confirmationBanner.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        _confirmationBanner.Spacing = 10;
        _confirmationBanner.Hidden = true;
        _confirmationText.WidthAnchor.ConstraintGreaterThanOrEqualTo(230).Active = true;
        chrome.InsertArrangedSubview(_confirmationBanner, 1);

        _entryPrompt = NSTextField.CreateWrappingLabel("");
        _entryPrompt.TextColor = NSColor.SecondaryLabel;
        _entryField = new NSTextField(new CGRect(0, 0, 340, 24));
        ((NSView)_entryField).AccessibilityLabel = "Command input";
        _entryField.WidthAnchor.ConstraintGreaterThanOrEqualTo(220).Active = true;
        var submit = NSButton.CreateButton("Apply", SubmitEntryPrompt);
        submit.KeyEquivalent = "\r";
        var dismiss = NSButton.CreateButton("Cancel", HideEntryPrompt);
        var actions = NSStackView.FromViews([submit, dismiss]);
        actions.Orientation = NSUserInterfaceLayoutOrientation.Horizontal;
        actions.Spacing = 8;
        _entryBanner = NSStackView.FromViews([_entryPrompt, _entryField, actions]);
        _entryBanner.Orientation = NSUserInterfaceLayoutOrientation.Vertical;
        _entryBanner.Spacing = 8;
        _entryBanner.Hidden = true;
        chrome.InsertArrangedSubview(_entryBanner, 2);
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
        _entryBanner.Hidden = false;
        _window.MakeKeyAndOrderFront(null);
        _window.MakeFirstResponder(_entryField);
        _entryField.SelectText(null);
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
