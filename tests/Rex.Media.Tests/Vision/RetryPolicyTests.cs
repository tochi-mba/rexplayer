using System.Net;
using Rex.Media.Vision;

namespace Rex.Media.Tests.Vision;

public sealed class RetryPolicyTests
{
    [Fact]
    public void PausesDoubleUpToTheLongest()
    {
        var policy = RetryPolicy.Default;

        Assert.Equal(TimeSpan.FromSeconds(2), policy.Delay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.Delay(2));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.Delay(3));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.Delay(5));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.Delay(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Delay(0));
    }

    [Fact]
    public void OnlyPassingFailuresAreRetriedAndOnlyAFewTimes()
    {
        var policy = RetryPolicy.Default;
        var dropped = new HttpRequestException("reset");

        Assert.True(policy.ShouldRetry(1, dropped));
        Assert.True(policy.ShouldRetry(3, dropped));
        Assert.False(policy.ShouldRetry(4, dropped));
        Assert.False(policy.ShouldRetry(1, new InvalidDataException()));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void ServerAnswersAreTransientOnlyWhenTheServerIsBusy(HttpStatusCode status, bool transient)
    {
        Assert.Equal(transient, RetryPolicy.IsTransient(new HttpRequestException("answer", null, status)));
    }

    [Fact]
    public void NetworkAndDiskHiccupsAreTransient()
    {
        Assert.True(RetryPolicy.IsTransient(new HttpRequestException("reset")));
        Assert.True(RetryPolicy.IsTransient(new IOException("ended early")));
        Assert.True(RetryPolicy.IsTransient(new TimeoutException()));
        Assert.True(RetryPolicy.IsTransient(new TaskCanceledException()));
        Assert.False(RetryPolicy.IsTransient(new UnauthorizedAccessException()));
    }
}
