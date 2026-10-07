using Rex.Media.Primitives;
using Rex.Media.TestKit;
using Rex.Media.Video;

namespace Rex.Media.Tests.Video;

public sealed class BorrowedPresenterTests
{
    [Fact]
    public void ABorrowedPresenterPassesPicturesThroughUntilItIsGivenBack()
    {
        using var owner = new RecordingVideoPresenter();
        using var frame = VideoFrame.Rent(PixelFormat.Bgra32, 2, 2);
        var borrowed = new BorrowedPresenter(owner);

        borrowed.Present(frame);
        borrowed.Dispose();
        borrowed.Present(frame);

        Assert.Equal(owner.Name, borrowed.Name);
        Assert.Equal(((IVideoPresenter)owner).Gpu, borrowed.Gpu);
        Assert.Single(owner.Shown);
        Assert.False(owner.Disposed);
        Assert.Throws<ArgumentNullException>(() => new BorrowedPresenter(null!));
    }
}
