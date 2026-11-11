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
        var candidate = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true };
        try
        {
            candidate.Client.Bind(new IPEndPoint(IPAddress.Parse(target.LocalAddress), target.LocalPort));
            candidate.Connect(IPAddress.Parse(target.RemoteAddress), target.RemotePort);
            socket = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
    public byte[] Read(byte function, byte channel = 0, byte segment = 0, byte side = 0, byte[]? requestBody = null) => Exchange(function, channel, segment, side, requestBody ?? [], 2, 0).GetAwaiter().GetResult();
    private byte[]? identityBody;
    public byte[]? ObservedIdentity => Volatile.Read(ref identityBody)?.ToArray();

    public byte[] ReadSnapshot(byte function)
    {
        if (function is not (0 or 6 or 27)) throw new ArgumentException("Only observed bulk reads are supported.");
        return Exchange(function, 0, 0, 0, [], 2, 1, observedBulk: true).GetAwaiter().GetResult();
    }

    private void ObserveIdentity(CvrMessage message)
    {
        if (message is { Function: 0, Status: 1, Channel: 3, Segment: 10, Side: 0 } &&
            message.Body.Length is 75 or 79)
            Volatile.Write(ref identityBody, message.Body.ToArray());
    }

    public byte[] Command(byte function,byte[] body,byte channel=0,byte side=0) => Exchange(function,channel,0,side,body,0,3).GetAwaiter().GetResult();
    public void Write(byte function, byte[] body, byte channel = 0, byte segment = 0, byte side = 0, byte messageStatus = 1)
    {
        serial.Wait();
        try
        {
            var packets=CvrWire.EncodeFragments(new(function,messageStatus,channel,segment,side,body));
            foreach(var packet in packets)
            {
                socket.Send(packet,packet.Length);
                if(packets.Count==1)continue;
                using var timeout=new CancellationTokenSource(timeoutMs);
                while(true)
                {
                    byte[] response;
                    try {response=socket.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult().Buffer;}
                    catch(OperationCanceledException){throw new TimeoutException("CVR fragment acknowledgement timed out. The write may be partial.");}
                    try
                    {
                        var header=CvrWire.Header(response);
                        if(header.Ack && response.AsSpan(4,4).SequenceEqual(packet.AsSpan(4,4)))break;
                        if(!header.Ack){var ack=response[..10];ack[8]=1;socket.Send(ack,ack.Length);}
                    }
                    catch(InvalidDataException) { }
                }
            }
        }
        finally { serial.Release(); }
    }
    private async Task<byte[]> Exchange(byte function, byte channel, byte segment, byte side, byte[] body, byte sendStatus, byte responseStatus, bool observedBulk = false)
    {
        // All backend operations hold their own gate; the transport also serializes standalone callers.
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            while (socket.Available > 0)
            {
                IPEndPoint? peer = null;
                var stale = socket.Receive(ref peer);
                try
                {
                    var header = CvrWire.Header(stale);
                    if (!header.Ack && header.Count == 1)
                    {
                        var message = CvrWire.DecodeInner(stale[10..]);
                        ObserveIdentity(message);
                        if (message.Function == 0)
                        {
                            var ack = stale[..10]; ack[8] = 1; socket.Send(ack, ack.Length);
                        }
                    }
                }
                catch (InvalidDataException) { }
            }
            var request = CvrWire.Encode(new(function, sendStatus, channel, segment, side, body));
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
                ObserveIdentity(message);
                if (observedBulk)
                {
                    var validLength = function == 0 ? message.Body.Length is 75 or 79 :
                        function == 27 ? message.Body.Length is 2232 or 2687 :
                        message.Body.Length is 32 or 56 or 72 or 76 or 88 or 92 or 96;
                    var responseChannel = function == 0 ? 3 : 32;
                    if (message.Function != function || message.Status != 1 || message.Channel != responseChannel ||
                        message.Segment != 10 || message.Side != 0 || !validLength) continue;
                    if (function == 27)
                    {
                        // Read completion only: no settings payload and no status-1 write.
                        var confirmation = CvrWire.Encode(new(27, 3, 0, 0, 0, []));
                        await socket.SendAsync(confirmation).ConfigureAwait(false);
                    }
                }
                else if (message.Function != function || message.Status != responseStatus || message.Channel != channel || message.Segment != segment || message.Side != side)
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
