using System.Runtime.InteropServices;
using CoreAnimation;

namespace Rex.Media.App.Mac;

/// <summary>
/// Shows a visualisation's BGRA pixels as they are: each frame becomes a bitmap image the layer
/// draws directly, with no encoding step between the stage and the screen. The stage is drawn at
/// the view's own shape in the screen's own pixels, so the picture fills the view exactly; the fill
/// keeps the picture's shape whatever happens between a resize and the next frame, and a canvas a
/// slow computer draws smaller is scaled up smoothly rather than in blocks.
/// </summary>
internal sealed class VisualStageView : NSView
{
    private static readonly CGColorSpace Srgb = CGColorSpace.CreateSrgb()!;
    private CGImage? _image;

    public VisualStageView()
    {
        WantsLayer = true;
        LayerContentsRedrawPolicy = NSViewLayerContentsRedrawPolicy.Never;
        Layer!.ContentsGravity = CALayer.GravityResizeAspectFill;
        Layer.MagnificationFilter = CALayer.FilterLinear;
        Layer.MinificationFilter = CALayer.FilterLinear;
        Layer.MasksToBounds = true;
    }

    /// <summary>Whether the picture is stretched to the view: the spectrogram's history is, as it is on Windows.</summary>
    public bool Stretches
    {
        get => Layer!.ContentsGravity == CALayer.GravityResize;
        set => Layer!.ContentsGravity = value ? CALayer.GravityResize : CALayer.GravityResizeAspectFill;
    }

    /// <summary>Whether a picture is showing.</summary>
    public bool HasPicture => _image is not null;

    /// <summary>
    /// Shows <paramref name="width"/> by <paramref name="height"/> pixels of <paramref name="bgra"/>.
    /// The pixels are copied, because the stage draws its next frame into the same buffer on another
    /// thread while this one is still on screen; the copy is freed when the layer lets the image go.
    /// </summary>
    public void Show(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        var size = width * height * 4;
        if (width <= 0 || height <= 0 || bgra.Length < size)
        {
            return;
        }

        var copy = Marshal.AllocHGlobal(size);
        Marshal.Copy(bgra, 0, copy, size);
        using var provider = new CGDataProvider(copy, size, Marshal.FreeHGlobal);
        // The stage writes every alpha byte as opaque, so the fourth byte is skipped rather than blended.
        var image = new CGImage(width, height, 8, 32, width * 4, Srgb,
            CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.NoneSkipFirst, provider, null, true, CGColorRenderingIntent.Default);
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        Layer!.Contents = image;
        CATransaction.Commit();
        _image?.Dispose();
        _image = image;
    }

    /// <summary>Takes the picture down and lets its pixels go.</summary>
    public void Clear()
    {
        if (_image is null)
        {
            return;
        }

        CATransaction.Begin();
        CATransaction.DisableActions = true;
        Layer!.Contents = null;
        CATransaction.Commit();
        _image.Dispose();
        _image = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }

        base.Dispose(disposing);
    }
}
