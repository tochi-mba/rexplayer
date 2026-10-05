using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Media.AppCore.Machine;

/// <summary>
/// The one JSON document a machine-mode command prints: <c>{"ok":true,"protocolVersion":1,
/// "command":"...","data":{...}}</c>, or <c>"ok":false</c> with an <c>"error"</c> object. Scripts and
/// agents rely on this shape, so it changes only with a new protocol version.
/// </summary>
public static class MachineEnvelope
{
    public const int ProtocolVersion = 1;

    /// <summary>Compact output, with text left readable: the document goes to a terminal or a parser, never into HTML.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Success(string command, JsonNode? data) => new JsonObject
    {
        ["ok"] = true,
        ["protocolVersion"] = ProtocolVersion,
        ["command"] = command,
        ["data"] = data,
    }.ToJsonString(Options);

    public static string Failure(string command, string errorType, string message) => new JsonObject
    {
        ["ok"] = false,
        ["protocolVersion"] = ProtocolVersion,
        ["command"] = command,
        ["error"] = new JsonObject
        {
            ["type"] = errorType,
            ["message"] = message,
        },
    }.ToJsonString(Options);
}
