using System.Text;
using Rex.Media.AppCore.Automation;
using Rex.Media.TestKit;

namespace Rex.Media.Tests.AppCore;

/// <summary>The player's pipe, over real named pipes with a name of the test's own.</summary>
public sealed class AutomationPipeTests
{
    private static readonly TimeSpan Connect = TimeSpan.FromSeconds(5);

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static string NewPipe() => "rexplayer-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    [Capability("TOOL-03")]
    public async Task ARequestReachesThePlayerAndItsReplyComesBack()
    {
        var pipe = NewPipe();
        using var stop = new CancellationTokenSource();
        var seen = new List<AutomationRequest>();
        var serving = AutomationPipe.ServeAsync(pipe, request =>
        {
            seen.Add(request);
            return Task.FromResult(new AutomationReply { Ok = true, State = "Playing", Title = "Sungba", Position = 1.5, Duration = 200 });
        }, stop.Token);

        var first = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "open", Paths = ["C:\\Music\\Sungba.mp3"], Enqueue = true }, Connect, TestContext.Current.CancellationToken);
        var second = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "run", Id = "play-pause" }, Connect, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await serving;

        Assert.Equal(new AutomationReply { Ok = true, State = "Playing", Title = "Sungba", Position = 1.5, Duration = 200 }, first);
        Assert.True(second!.Ok);
        Assert.Equal(["C:\\Music\\Sungba.mp3"], seen[0].Paths);
        Assert.True(seen[0].Enqueue);
        Assert.Equal(("run", "play-pause"), (seen[1].Command, seen[1].Id));
    }

    [Fact]
    public async Task AClientThatHangsUpWithoutAWordLeavesThePipeWorking()
    {
        var pipe = NewPipe();
        using var stop = new CancellationTokenSource();
        var serving = AutomationPipe.ServeAsync(pipe, _ => Task.FromResult(new AutomationReply { Ok = true }), stop.Token);

        for (var i = 0; i < 3; i++)
        {
            await using var rude = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await rude.ConnectAsync(Connect, TestContext.Current.CancellationToken);
        }

        var reply = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "status" }, Connect, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await serving;

        Assert.True(reply!.Ok);
    }

    [Fact]
    [Capability("TOOL-06")]
    public async Task WithNoPlayerListeningASecondLaunchKnowsAtOnce()
    {
        Assert.Null(await AutomationPipe.SendAsync(NewPipe(), new AutomationRequest { Command = "status" }, TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyOnePlayerCanOwnThePipe()
    {
        var pipe = NewPipe();
        using var stop = new CancellationTokenSource();
        var serving = AutomationPipe.ServeAsync(pipe, _ => Task.FromResult(new AutomationReply { Ok = true }), stop.Token);

        // Refused while starting, not later: the owner learns at once that another player is running.
        Assert.ThrowsAny<IOException>(() => { _ = AutomationPipe.ServeAsync(pipe, _ => Task.FromResult(new AutomationReply()), stop.Token); });

        await stop.CancelAsync();
        await serving;
    }

    [Theory]
    [InlineData("", "The request was empty.")]
    [InlineData("{ not json\n", "The request was not valid JSON: ")]
    [InlineData("null\n", "The request was not a request.")]
    [InlineData("{\"protocolVersion\":2,\"command\":\"status\"}\n", "This player speaks protocol 1, not 2.")]
    public async Task UnreadableRequestsAreAnsweredWithTheReason(string sent, string reason)
    {
        var reply = await Answer(Encoding.UTF8.GetBytes(sent));

        Assert.False(reply.Ok);
        Assert.StartsWith(reason, reply.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestThatIsTooLongIsRefusedBeforeItCanFillMemory()
    {
        var reply = await Answer(Encoding.UTF8.GetBytes(new string('x', AutomationPipe.MaxLineBytes + 10)));

        Assert.Equal($"The request is longer than {AutomationPipe.MaxLineBytes} bytes.", reply.Error);
    }

    [Fact]
    public async Task ARequestDroppedBeforeItsAnswerIsSentAgain()
    {
        var pipe = NewPipe();
        var server = Task.Run(
            async () =>
            {
                // Hangs up on the first connection, as a busy player on Linux does; answers the second.
                await using (var rude = Server(pipe))
                {
                    await rude.WaitForConnectionAsync(TestContext.Current.CancellationToken);
                }

                await using var polite = Server(pipe);
                await polite.WaitForConnectionAsync(TestContext.Current.CancellationToken);
                await AutomationPipe.AnswerAsync(polite, _ => Task.FromResult(new AutomationReply { Ok = true, State = "Playing" }), TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);

        var reply = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "status" }, Connect, TestContext.Current.CancellationToken);
        await server;

        Assert.Equal("Playing", reply!.State);
    }

    [Fact]
    public async Task APlayerThatNeverAnswersIsGivenUpOnAfterThreeTries()
    {
        var pipe = NewPipe();
        var server = Task.Run(
            async () =>
            {
                // Takes each request, then hangs up without a word.
                for (var i = 0; i < 3; i++)
                {
                    await using var rude = Server(pipe);
                    await rude.WaitForConnectionAsync(TestContext.Current.CancellationToken);
                    await AutomationPipe.ReadLineAsync(rude, TestContext.Current.CancellationToken);
                }
            },
            TestContext.Current.CancellationToken);

        var reply = await AutomationPipe.SendAsync(pipe, new AutomationRequest { Command = "status" }, Connect, TestContext.Current.CancellationToken);
        await server;

        Assert.Null(reply);
    }

    private static System.IO.Pipes.NamedPipeServerStream Server(string pipe) =>
        new(pipe, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);

    [Fact]
    public async Task ARequestThatLeavesOutItsPathsHasNone()
    {
        IReadOnlyList<string>? paths = null;

        await Answer("""{"command":"open"}"""u8.ToArray(), request =>
        {
            paths = request.Paths;
            return new AutomationReply { Ok = true };
        });

        Assert.Empty(paths!);
    }

    [Fact]
    public async Task APlayerThatFailsToAnswerSaysSoAndKeepsListening()
    {
        var reply = await Answer("{\"command\":\"status\"}\n"u8.ToArray(), _ => throw new InvalidOperationException("the window is closing"));

        Assert.Equal("The player could not do that: the window is closing", reply.Error);
    }

    [Fact]
    public async Task ALastLineWithoutANewlineStillCounts()
    {
        var reply = await Answer("{\"command\":\"status\"}"u8.ToArray(), _ => new AutomationReply { Ok = true, State = "Idle" });

        Assert.True(reply.Ok, reply.Error);
        Assert.Equal("Idle", reply.State);
    }

    [Fact]
    public void ThePipeNameIsTheUsersOwnUnlessATestChoosesOne()
    {
        var name = AutomationPipe.NameForCurrentUser;
        Assert.Matches("^rexplayer-[0-9a-f]{16}$", Environment.GetEnvironmentVariable("REXPLAYER_PIPE_NAME") is { Length: > 0 } ? "rexplayer-0000000000000000" : name);
        Assert.Equal(name, AutomationPipe.NameForCurrentUser);
        Assert.False(AutomationReply.Failed("why").Ok);
    }

    [Fact]
    public async Task ArgumentsAreRequired()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => AutomationPipe.SendAsync("x", null!, Connect, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => AutomationPipe.AnswerAsync(null!, _ => Task.FromResult(new AutomationReply()), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => AutomationPipe.AnswerAsync(new MemoryStream(), null!, TestContext.Current.CancellationToken));
    }

    /// <summary>Answers <paramref name="sent"/> in memory and reads the reply back.</summary>
    private static async Task<AutomationReply> Answer(byte[] sent, Func<AutomationRequest, AutomationReply>? handle = null)
    {
        var connection = new Duplex(sent);
        await AutomationPipe.AnswerAsync(connection, request => Task.FromResult((handle ?? (_ => new AutomationReply { Ok = true }))(request)), TestContext.Current.CancellationToken);
        var line = await AutomationPipe.ReadLineAsync(new MemoryStream(connection.Written.ToArray()), TestContext.Current.CancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<AutomationReply>(line!, Web)!;
    }

    /// <summary>A connection whose incoming bytes are fixed and whose outgoing bytes are kept.</summary>
    private sealed class Duplex(byte[] incoming) : Stream
    {
        private readonly MemoryStream _in = new(incoming);

        public MemoryStream Written { get; } = new();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _in.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Written.Write(buffer, offset, count);
    }
}
