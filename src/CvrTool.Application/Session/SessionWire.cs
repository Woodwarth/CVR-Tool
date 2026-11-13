// Application / Session protocol
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: frame bounded JSON messages on a local user-only named pipe.
// Input: backend calls from UI or MCP; no CVR network packets.
// State: one owner process serializes mutations and revisions.
// Scope: the same transport runs on Windows and macOS.
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CvrTool.Application;

public static class SessionDefaults
{
    public static string PipeName => "cvr-tool-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..12];

    // macOS includes its long per-user temporary directory in the Unix socket path.
    // Hash arbitrary session labels rather than using them as filesystem names.
    public static string Resolve(string? name) => name is null || name == PipeName ? PipeName
        : "cvr-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\0" + name)))[..20];
}
internal sealed record SessionRequest(string Method, JsonElement Arguments);
internal sealed record SessionReply(JsonElement? Result, string? Error = null, string? ErrorType = null);
internal sealed record Revisioned<T>(T State, long Revision);
internal static class SessionWire
{
    public const int MaximumBytes = 1024 * 1024;
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("Session message exceeds 1 MiB.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumBytes)
            throw new InvalidDataException("Invalid session message length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty session message.");
    }
}
