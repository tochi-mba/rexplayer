namespace Rex.Media.App.Mac;

/// <summary>Reusable nonmodal confirmation banner: a decision does not block playback or the run loop.</summary>
public sealed partial class MainWindow
{
    private NSStackView? _confirmationBanner;
    private NSTextField? _confirmationText;
    private Action? _confirmationAccept;

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

    private void HideConfirmation()
    {
        _confirmationAccept = null;
        if (_confirmationBanner is not null)
        {
            _confirmationBanner.Hidden = true;
        }
    }
}
