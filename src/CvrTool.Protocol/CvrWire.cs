// Protocol / CVR wire framing
// Purpose: encode independent CVR requests and validate replies.
// Units: packed little-endian fields; inner length is big-endian.
// State: link bits are never used as a transaction identifier.
// Scope: explicit peers only; no discovery or firmware operations.
using System.Buffers.Binary;

namespace CvrTool.Protocol;

public sealed record CvrMessage(byte Function, byte Status, byte Channel, byte Segment, byte Side, byte[] Body);

public static class CvrWire
{
    public static byte[] Encode(CvrMessage message)
    {
        var inner = new byte[13 + message.Body.Length];
        if (inner.Length > 450)
            throw new ArgumentException("Outgoing payload exceeds one fragment.");
        inner[0] = 0x55;
        inner[1] = message.Function;
        inner[2] = message.Status;
        inner[3] = message.Channel;
        inner[4] = message.Segment;
        inner[9] = message.Side;
        message.Body.CopyTo(inner, 10);
        BinaryPrimitives.WriteUInt16BigEndian(inner.AsSpan(inner.Length - 3), (ushort)inner.Length);
        inner[^1] = unchecked((byte)inner.Take(inner.Length - 1).Sum(b => b));
        var packet = new byte[10 + inner.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(packet, 55555);
        packet[4] = packet[7] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(5), (ushort)inner.Length);
        inner.CopyTo(packet, 10);
        return packet;
    }

    public static CvrMessage DecodeInner(byte[] inner)
    {
        if (inner.Length < 13 || inner[0] != 0x55 ||
            BinaryPrimitives.ReadUInt16BigEndian(inner.AsSpan(inner.Length - 3)) != inner.Length ||
            unchecked((byte)inner.Take(inner.Length - 1).Sum(b => b)) != inner[^1])
            throw new InvalidDataException("Invalid CVR message length or checksum.");
        return new(inner[1], inner[2], inner[3], inner[4], inner[9], inner[10..^3]);
    }

    public static (int Count, int Last, int Index, bool Ack) Header(byte[] packet)
    {
        if (packet.Length < 10 || BinaryPrimitives.ReadUInt16LittleEndian(packet) != 55555)
            throw new InvalidDataException("Invalid CVR packet header.");
        int count = packet[4], last = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(5)), index = packet[7];
        if (count is < 1 or > 64 || last is < 1 or > 450 || index < 1 || index > count || packet[8] > 1 ||
            packet.Length != (packet[8] == 1 ? 10 : 10 + (index == count ? last : 450)))
            throw new InvalidDataException("Invalid CVR fragment bounds.");
        return (count, last, index, packet[8] == 1);
    }
}
