// Protocol / Bounded reply assembly
// Purpose: retain fragmented replies while unrelated announcements arrive.
// Units: at most four messages of at most 64 fragments, each at most 450 bytes.
// State: duplicate fragments must match; complete corrupt messages are discarded.
// Scope: one query lifetime; the wire protocol provides no transaction identifier.
namespace CvrTool.Protocol;

internal sealed class CvrReplyAssembler
{
    private const int MaximumAssemblies = 4;
    private sealed record Partial(byte[][] Parts, long Order);
    private readonly Dictionary<string, Partial> pending = new();
    private long sequence;

    public CvrMessage? Accept(byte[] packet)
    {
        var header = CvrWire.Header(packet);
        if (header.Ack) return null;
        byte[] inner;
        if (header.Count == 1) inner = packet[10..];
        else
        {
            var key = Convert.ToHexString(packet.AsSpan(0, 7));
            if (!pending.TryGetValue(key, out var current))
            {
                if (pending.Count == MaximumAssemblies) pending.Remove(pending.MinBy(p => p.Value.Order).Key);
                pending[key] = current = new(new byte[header.Count][], sequence++);
            }
            var parts = current.Parts;
            var part = packet[10..];
            if (parts[header.Index - 1] is { } existing && !existing.SequenceEqual(part))
            {
                pending.Remove(key);
                return null;
            }
            parts[header.Index - 1] = part;
            if (parts.Any(p => p is null)) return null;
            pending.Remove(key);
            inner = parts.SelectMany(p => p).ToArray();
        }
        try { return CvrWire.DecodeInner(inner); }
        catch (InvalidDataException) { return null; }
    }
}
