namespace Rex.Media.App.Mac;

/// <summary>
/// The stage while music plays: black in every theme, as the Windows stage is, with its parts
/// placed by frame each time it is laid out (the shared stage layout decides where), top-left first.
/// </summary>
internal sealed class MusicStageHost : NSView
{
    private readonly Action<MusicStageHost> _layOut;

    public MusicStageHost(Action<MusicStageHost> layOut)
    {
        _layOut = layOut;
        WantsLayer = true;
        Layer!.BackgroundColor = MacTheme.Stage.CGColor;
        Layer.MasksToBounds = true;
        // The stage is dark whatever the window's theme, so whatever stock control sits on it is too.
        Appearance = NSAppearance.GetAppearance(NSAppearance.NameDarkAqua);
    }

    public override bool IsFlipped => true;

    public override void Layout()
    {
        base.Layout();
        _layOut(this);
    }
}

/// <summary>The cover on the stage: the picture with rounded corners, lifted off the black by a soft shadow.</summary>
internal sealed class MusicCoverView : NSView
{
    private readonly NSImageView _picture;

    public MusicCoverView()
    {
        WantsLayer = true;
        var layer = Layer!;
        layer.MasksToBounds = false;
        layer.ShadowColor = MacTheme.Color(0xFF000000).CGColor;
        layer.ShadowOpacity = 0.5f;
        layer.ShadowRadius = 16;
        layer.ShadowOffset = new CGSize(0, -6);
        _picture = new NSImageView
        {
            ImageScaling = NSImageScale.ProportionallyUpOrDown,
            WantsLayer = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        _picture.Layer!.CornerRadius = MacTheme.Radius(2);
        _picture.Layer.MasksToBounds = true;
        _picture.Layer.BackgroundColor = MacTheme.OverlayFill.CGColor;
        _picture.Layer.BorderColor = MacTheme.OverlayFill.CGColor;
        _picture.Layer.BorderWidth = 1;
        AddSubview(_picture);
    }

    /// <summary>The cover shown; none leaves an empty rounded square.</summary>
    public NSImage? Image
    {
        get => _picture.Image;
        set => _picture.Image = value;
    }

    /// <summary>What a screen reader calls the cover.</summary>
    public string CoverDescription
    {
        get => ((NSView)_picture).AccessibilityLabel ?? "";
        set => ((NSView)_picture).AccessibilityLabel = value;
    }

    public override void SetFrameSize(CGSize newSize)
    {
        base.SetFrameSize(newSize);
        _picture.Frame = new CGRect(0, 0, newSize.Width, newSize.Height);
        Layer!.ShadowPath = CGPath.FromRoundedRect(_picture.Frame, MacTheme.Radius(2), MacTheme.Radius(2));
    }
}
