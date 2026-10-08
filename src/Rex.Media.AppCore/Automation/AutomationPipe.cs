using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.Media.AppCore.Automation;

/// <summary>
/// What a second launch or a script asks the running player (TOOL-03): <c>open</c> media (with
/// <see cref="Paths"/>, joining the playlist when <see cref="Enqueue"/>), <c>run</c> the command
/// <see cref="Id"/>, or tell its <c>status</c>. A request that leaves out its protocol version (a
/// script written by hand) is taken to speak the current one.
/// </summary>
public sealed record AutomationRequest
{
    public int ProtocolVersion { get; init; } = AutomationPipe.ProtocolVersion;

    public required string Command { get; init; }

    public IReadOnlyList<string> Paths { get; init; } = [];

    public bool Enqueue { get; init; }

    public string? Id { get; init; }

    /// <summary>When the launch sending files started; launches moments apart are one choice in Explorer.</summary>
    public DateTimeOffset? LaunchedAt { get; init; }
}

/// <summary>The player's answer: whether it did it, or why not, and what it is playing.</summary>
public sealed record AutomationReply
{
    public int ProtocolVersion { get; init; } = AutomationPipe.ProtocolVersion;

    public bool Ok { get; init; }

    public string? Error { get; init; }

    public string? State { get; init; }

    public string? Title { get; init; }

    public double? Position { get; init; }

    public double? Duration { get; init; }

    public static AutomationReply Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// The running player's pipe: one JSON line in, one JSON line out per connection. It is the
/// current user's only (another account cannot connect), requests are capped at
/// <see cref="MaxLineBytes"/>, and a client that connects but says nothing is dropped after
/// <see cref="RequestTimeout"/>, so nothing that connects can hold the player up.
/// </summary>
public static class AutomationPipe
{
    public const int ProtocolVersion = 1;

    public const int MaxLineBytes = 64 * 1024;

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The pipe for this user: REXPLAYER_PIPE_NAME when set (tests), else derived from the user's name.</summary>
    public static string NameForCurrentUser =>
        Environment.GetEnvironmentVariable("REXPLAYER_PIPE_NAME") is { Length: > 0 } name
            ? name
            : "rexplayer-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16].ToLowerInvariant();

    /// <summary>
    /// Sends <paramref name="request"/> to the player listening on <paramref name="pipeName"/>; null
    /// when no player answers within <paramref name="connectTimeout"/>. A connection dropped before
    /// any answer is tried again twice: on Linux a pipe is a socket, and connections still queued
    /// when the player finishes with another are reset rather than kept.
    /// </summary>
    public static async Task<AutomationReply?> SendAsync(string pipeName, AutomationRequest request, TimeSpan connectTimeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (connected, reply) = await SendOnceAsync(pipeName, request, connectTimeout, cancellationToken).ConfigureAwait(false);
                if (!connected || reply is not null)
                {
                    return reply;
                }
            }
            catch (IOException)
            {
                // Reset before an answer came: the request was not taken, so sending it again is safe.
            }

            if (attempt >= Attempts)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>How many times a request is sent before the sender gives up.</summary>
    private const int Attempts = 3;

    /// <summary>One try: whether a player was there, and its reply (null when it hung up without one).</summary>
    private static async Task<(bool Connected, AutomationReply? Reply)> SendOnceAsync(string pipeName, AutomationRequest request, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(connectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return (false, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        await WriteLineAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, AutomationJson.Default.AutomationRequest), timeout.Token).ConfigureAwait(false);
        var line = await ReadLineAsync(pipe, timeout.Token).ConfigureAwait(false);
        return (true, line is null ? null : JsonSerializer.Deserialize(line, AutomationJson.Default.AutomationReply));
    }

    /// <summary>
    /// Answers one connection: reads its request, lets <paramref name="handle"/> act on it and
    /// writes the reply. Unreadable requests are answered with the reason, never thrown.
    /// </summary>
    public static async Task AnswerAsync(Stream connection, Func<AutomationRequest, Task<AutomationReply>> handle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(handle);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        AutomationReply reply;
        try
        {
            reply = await ReadLineAsync(connection, timeout.Token).ConfigureAwait(false) is not { } line
                ? AutomationReply.Failed("The request was empty.")
                : JsonSerializer.Deserialize(line, AutomationJson.Default.AutomationRequest) is not { } request
                    ? AutomationReply.Failed("The request was not a request.")
                    : request.ProtocolVersion is not (0 or ProtocolVersion)
                        ? AutomationReply.Failed($"This player speaks protocol {ProtocolVersion}, not {request.ProtocolVersion}.")
                        : await Handle(handle, request with { Paths = request.Paths ?? [] }).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            reply = AutomationReply.Failed("The request was not valid JSON: " + ex.Message);
        }
        catch (InvalidDataException ex)
        {
            reply = AutomationReply.Failed(ex.Message);
        }

        await WriteLineAsync(connection, JsonSerializer.SerializeToUtf8Bytes(reply, AutomationJson.Default.AutomationReply), timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Listens on <paramref name="pipeName"/> until cancelled, answering one connection at a time.
    /// Throws <see cref="IOException"/> at once when another player already owns the pipe.
    /// </summary>
    public static Task ServeAsync(string pipeName, Func<AutomationRequest, Task<AutomationReply>> handle, CancellationToken cancellationToken)
    {
        // The first server is made before returning, so a pipe someone else owns fails the caller now.
        var first = NewServer(pipeName);
        return Task.Run(
            async () =>
            {
                var server = first;
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        await AnswerAsync(server, handle, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException)
                    {
                        // A client that hung up or timed out, or the player closing: the next connection is unaffected.
                    }
                    finally
                    {
                        await server.DisposeAsync().ConfigureAwait(false);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        server = NewServer(pipeName);
                    }
                }
            },
            CancellationToken.None);
    }

    /// <summary>The player's answer, or why it could not give one: a failure in the player must not silence the pipe.</summary>
    private static async Task<AutomationReply> Handle(Func<AutomationRequest, Task<AutomationReply>> handle, AutomationRequest request)
    {
        try
        {
            return await handle(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return AutomationReply.Failed("The player could not do that: " + ex.Message);
        }
    }

    private static NamedPipeServerStream NewServer(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task WriteLineAsync(Stream stream, byte[] json, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One line, without its newline; null at the end of the stream before any byte; too long a line is refused.</summary>
    internal static async Task<byte[]?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0)
            {
                return line.Length == 0 ? null : line.ToArray();
            }

            if (one[0] == (byte)'\n')
            {
                return line.ToArray();
            }

            if (line.Length >= MaxLineBytes)
            {
                throw new InvalidDataException($"The request is longer than {MaxLineBytes} bytes.");
            }

            line.WriteByte(one[0]);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AutomationRequest))]
[JsonSerializable(typeof(AutomationReply))]
internal sealed partial class AutomationJson : JsonSerializerContext;
