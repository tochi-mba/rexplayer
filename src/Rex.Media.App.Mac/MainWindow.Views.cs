using Rex.Media.Diagnostics;
using Rex.Media.Video.Metal;

namespace Rex.Media.App.Mac;

public sealed partial class MainWindow
{
    /// <summary>
    /// The picture: a view backed by a CAMetalLayer the presenter draws into, its drawable kept at
    /// the view's size in the screen's own pixels.
    /// </summary>
    private sealed class PictureView : NSView
    {
        private readonly CoreAnimation.CAMetalLayer _layer = new();

        public PictureView(RexLog log)
        {
            WantsLayer = true;
            Layer = _layer;
            _layer.BackgroundColor = NSColor.Black.CGColor;
            try
            {
                Presenter = MetalPresenter.ForLayer(_layer);
            }
            catch (InvalidOperationException ex)
            {
                log.Error("window", "Pictures cannot be shown: " + ex.Message);
            }

            if (Presenter is null)
            {
                log.Warning("window", "This Mac has no Metal device; videos play their sound only.");
            }
        }

        public MetalPresenter? Presenter { get; }

        public override void Layout()
        {
            base.Layout();
            var scale = Window?.BackingScaleFactor ?? 1;
            _layer.ContentsScale = scale;
            Presenter?.Resize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            Presenter?.Redraw();
        }
    }

    /// <summary>The window's content: takes files and folders dropped on it.</summary>
    private sealed class DropView : NSView
    {
        private readonly Action<IReadOnlyList<string>, bool> _open;
        private readonly Action<NSEvent> _wheel;
        private readonly Action<NSEvent> _drag;
        private readonly Action<NSEvent> _click;
        private readonly Action<NSEvent> _otherClick;
        private readonly Action _mouseMoved;

        public DropView(Action<IReadOnlyList<string>, bool> open, Action<NSEvent> wheel,
            Action<NSEvent> drag, Action<NSEvent> click, Action<NSEvent> otherClick, Action mouseMoved)
        {
            _open = open;
            _wheel = wheel;
            _drag = drag;
            _click = click;
            _otherClick = otherClick;
            _mouseMoved = mouseMoved;
            RegisterForDraggedTypes([NSPasteboardType.FileUrl.GetConstant()!]);
        }

        public override void ScrollWheel(NSEvent theEvent) => _wheel(theEvent);

        public override void MouseDragged(NSEvent theEvent) => _drag(theEvent);

        public override void MouseDown(NSEvent theEvent)
        {
            _click(theEvent);
            base.MouseDown(theEvent);
        }

        public override void OtherMouseDown(NSEvent theEvent)
        {
            _otherClick(theEvent);
            base.OtherMouseDown(theEvent);
        }

        public override void MouseMoved(NSEvent theEvent)
        {
            _mouseMoved();
            base.MouseMoved(theEvent);
        }

        public override NSDragOperation DraggingEntered(INSDraggingInfo sender) => NSDragOperation.Copy;

        public override bool PerformDragOperation(INSDraggingInfo sender)
        {
            var urls = sender.DraggingPasteboard.ReadObjectsForClasses([new ObjCRuntime.Class(typeof(NSUrl))], null)?.OfType<NSUrl>().Select(url => url.Path!).ToList();
            if (urls is not { Count: > 0 })
            {
                return false;
            }

            var modifiers = NSApplication.SharedApplication.CurrentEvent?.ModifierFlags ?? 0;
            _open(urls, (modifiers & NSEventModifierMask.CommandKeyMask) != 0);
            return true;
        }
    }
}
