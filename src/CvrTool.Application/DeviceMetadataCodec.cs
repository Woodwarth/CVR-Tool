// Application / Extended identity decoder
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: decode the observed function-71 operating-time counter independently.
// Units: unsigned little-endian minutes at offset 74 in a 93-byte payload.
// State: the embedded firmware/model header must match the connected device.
// Scope: no counter resets, inferred uptime or manufacturer source is included.
using System.Buffers.Binary;
using System.Text;

namespace CvrTool.Application;

public static class DeviceMetadataCodec
{
    public static uint OperatingMinutes(byte[] body, string firmwareHeader)
    {
        if (body.Length != 93 || Encoding.Latin1.GetString(body, 42, 32).TrimEnd('\0') != firmwareHeader)
            throw new NotSupportedException("Extended identity does not match the connected device.");
        return BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(74, 4));
    }
    public static string FormatOperatingTime(uint? minutes) => minutes is { } value ? $"{value / 60} h {value % 60:00} min" : "—";
}
