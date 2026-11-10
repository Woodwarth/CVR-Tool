// Protocol / Explicit CVR connection
// Purpose: serialize exchanges with one manually selected amplifier.
// Input: validated IPv4 target; no broadcasts, DNS or automatic retries.
// State: fragments are acknowledged, bounded and reassembled before validation.
// Scope: transport ACK is not write success; callers must read back changes.
using System.Net;
using System.Net.Sockets;

namespace CvrTool.Protocol;

public sealed class CvrUdpClient : IDisposable
{
    private readonly UdpClient socket;
    private readonly int timeoutMs;
    public CvrUdpClient(NetworkTarget target)
    {
        target = NetworkPreparation.Validate(target).Target;
        timeoutMs = target.TimeoutMs;
        socket = new UdpClient(new IPEndPoint(IPAddress.Parse(target.LocalAddress), target.LocalPort));
        socket.Connect(IPAddress.Parse(target.RemoteAddress), target.RemotePort);
    }
    public byte[] Read(byte function, byte channel = 0, byte segment = 0, byte side = 0) => Exchange(function, channel, segment, side).GetAwaiter().GetResult();
    public void Write(byte function, byte[] body, byte channel = 0, byte segment = 0, byte side = 0)
    {
        serial.Wait();
        try
        {
            var packet = CvrWire.Encode(new(function, 1, channel, segment, side, body));
            socket.Send(packet, packet.Length);
        }
        finally { serial.Release(); }
    }
    private async Task<byte[]> Exchange(byte function, byte channel, byte segment, byte side)
    {
        // All backend operations hold their own gate; the transport also serializes standalone callers.
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            while (socket.Available > 0)
            {
                IPEndPoint? peer = null;
                socket.Receive(ref peer);
            }
            var request = CvrWire.Encode(new(function, 2, channel, segment, side, []));
            await socket.SendAsync(request).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(timeoutMs);
            byte[][]? fragments = null;
            byte[]? identity = null;
            int last = 0;
            while (true)
            {
                byte[] packet;
                try
                {
                    packet = (await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false)).Buffer;
                }
                catch (OperationCanceledException) { throw new TimeoutException("CVR response timed out. Device state was not confirmed."); }
                (int Count, int Last, int Index, bool Ack) header;
                try
                {
                    header = CvrWire.Header(packet);
                }
                catch (InvalidDataException) { continue; }
                if (header.Ack)
                    continue;
                var ack = packet[..10];
                ack[8] = 1;
                await socket.SendAsync(ack).ConfigureAwait(false);
                // Keep one bounded assembly; fragments may arrive before fragment one.
                if (identity is null || !identity.AsSpan().SequenceEqual(packet.AsSpan(0, 7)))
                {
                    fragments = new byte[header.Count][];
                    identity = packet[..7];
                    last = header.Last;
                }
                if (fragments is null || header.Count != fragments.Length || header.Last != last)
                    continue;
                fragments[header.Index - 1] = packet[10..];
                if (fragments.Any(f => f is null))
                    continue;
                var message = CvrWire.DecodeInner(fragments.SelectMany(f => f).ToArray());
                fragments = null;
                identity = null;
                if (message.Function != function || message.Status != 0 || message.Channel != channel || message.Segment != segment || message.Side != side)
                    continue;
                return message.Body;
            }
        }
        finally { serial.Release(); }
    }
    private readonly SemaphoreSlim serial = new(1, 1);
    public void Dispose()
    {
        socket.Dispose();
        serial.Dispose();
    }
}
