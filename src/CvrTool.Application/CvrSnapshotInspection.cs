// Application / Controller protocol inspection
// Purpose: compare individual read replies with the last full settings snapshot.
// Scope: one selected function and channel per request, using the shared LAN owner.
// Results describe reply contents and comparisons, never a verified write.
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public string InspectProtocol(string id, int function, int channel, int segment)
    {
        lock (ioGate)
        {
            byte[] snapshot;
            lock (gate)
            {
                CheckId(id);
                CheckChannel(channel);
                snapshot = native;
            }
            if (function == 16) return "Factory reset is protected.";
            if (function is not (43 or 50 or 51 or 52 or 53 or 54 or 55 or 57 or 59 or 62 or 82 or 88 or 91 or 93))
                throw new ArgumentOutOfRangeException(nameof(function));
            if (segment < 0 || segment > (function == 62 ? 2 : 0))
                throw new ArgumentOutOfRangeException(nameof(segment));
            var index = channel - 1;
            int address = function switch { 50 => index / 2, 82 or 59 => 0, _ => index };
            byte side = function is 43 or 50 or 52 or 54 or 55 or 57 or 91 ? (byte)1 : (byte)0;
            byte[] reply = [];
            CvrEndpointGate.UseClient(target, client => reply = function == 59 ? client.ReadPresetNames() :
                client.ReadInspection((byte)function, (byte)address, (byte)segment, side));
            int offset = 36 + index * 515;
            string Match(int start, int size) => reply.Length != size ? "Unexpected layout" :
                reply.AsSpan().SequenceEqual(snapshot.AsSpan(start, size)) ? "Matches last full settings" : "Differs from last full settings";
            string result = function switch
            {
                43 => $"FIR: {ProcessingWire.DecodeFir(channel, reply, true).Coefficients.Count} taps",
                50 when reply.Length == 1 && reply[0] <= 1 => (reply[0] == 0 ? "Bridge" : "Stereo") + " · " + Match(34 + address, 1),
                51 when reply.Length == 515 => reply.AsSpan(0, 373).SequenceEqual(snapshot.AsSpan(offset, 373))
                    ? "Channel copy · first 373 bytes match channel settings" : "Channel copy · channel settings differ",
                53 when reply.Length == 105 => "Dynamic EQ · " + Match(offset + 410, 105),
                57 when reply.Length == 2415 => "Speaker profile received",
                52 when reply.Length == 99 => "Seven filter records; profile mapping pending",
                52 => Match(offset + 226, 141),
                54 => Match(offset + 72, 13),
                55 => Match(offset + 59, 13),
                59 => $"{ProcessingWire.PresetSlots(reply).Length} entries in device name table",
                62 when reply.Length == 8 => $"Gain {ProcessingWire.Float(reply, 0):0.##} dB · Delay {ProcessingWire.Float(reply, 4):0.##} ms · {Match(offset + 8 * segment, 8)}",
                82 when reply.Length == 404 => "RTA bins: " + string.Join(", ", Enumerable.Range(0, 30).Select(i => ProcessingWire.Float(reply, 92 + 4 * i).ToString("0.##"))),
                88 when reply.Length == 1 => $"Input delay switch code {reply[0]}",
                91 when reply.Length == 30 => $"Configured limiter load: {ProcessingWire.Float(reply, 26):0.##} Ω · " +
                    (reply.AsSpan(0, 26).SequenceEqual(snapshot.AsSpan(offset + 59, 26))
                        ? "Limiter blocks match last full settings" : "Limiter blocks differ from last full settings"),
                93 when reply.Length == 4 => $"Input group gain {ProcessingWire.Float(reply, 0):0.##} dB",
                _ => "Unexpected layout"
            };
            return $"ID {function} · {reply.Length} bytes · {result}";
        }
    }
}
