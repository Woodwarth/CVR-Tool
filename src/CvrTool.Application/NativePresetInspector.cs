// Application / Native presets — bounded file inspection; no parameter decoding or import.
// Purpose: keep device behavior shared between desktop and MCP.
// Input: explicit operations validated at the application boundary.
// State: revisions protect pending edits; backend owners serialize mutations.
// Scope: transport and physical-validation limits are documented separately.
using System.Security.Cryptography;
using System.Text;

namespace CvrTool.Application;

public sealed record NativePresetMarker(int Offset, string Text);
public sealed record NativePresetInspection(int ByteLength, string Sha256, string? ModelHint, IReadOnlyList<NativePresetMarker> Markers)
{
    public bool CanApply => false;
}

/// <summary>Read-only observations, not a decoder for DSP parameter values.</summary>
public static class NativePresetInspector
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public static NativePresetInspection Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 32 || bytes.Length > MaximumBytes)
            throw new InvalidDataException("Unsupported file size (32 bytes to 8 MiB).");
        var prefix = bytes[..Math.Min(128, bytes.Length)];
        var terminator = prefix.IndexOf((byte)0);
        string? model = null;
        if (terminator > 0 && prefix[..terminator].IndexOfAnyExceptInRange((byte)32, (byte)126) < 0)
        {
            var header = Encoding.ASCII.GetString(prefix[..terminator]);
            foreach (var candidate in new[]
            {
                "DSP-1004",
                "DSP-2002",
                "DSP-1502"
            }

            )
                if (header.EndsWith("-" + candidate, StringComparison.Ordinal))
                    model = candidate;
        }

        var markers = new List<NativePresetMarker>();
        ReadOnlySpan<byte> marker = "FIR=="u8;
        for (var offset = 0; offset <= bytes.Length - marker.Length; offset++)
        {
            if (!bytes.Slice(offset, marker.Length).SequenceEqual(marker))
                continue;
            var end = offset + marker.Length;
            while (end < bytes.Length && end - offset < 16 && bytes[end] is >= (byte)'0' and <= (byte)'9')
                end++;
            if (end > offset + marker.Length && (end == bytes.Length || bytes[end] == 0))
            {
                markers.Add(new(offset, Encoding.ASCII.GetString(bytes[offset..end])));
                if (markers.Count == 32)
                    break;
            }
        }

        return new(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), model, markers.AsReadOnly());
    }

    public static async Task<NativePresetInspection> InspectAsync(Stream stream, CancellationToken cancellation = default)
    {
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false);
            if (count == 0)
                break;
            if (content.Length + count > MaximumBytes)
                throw new InvalidDataException("Preset exceeds 8 MiB.");
            content.Write(buffer, 0, count);
        }

        return Inspect(content.GetBuffer().AsSpan(0, (int)content.Length));
    }
}
