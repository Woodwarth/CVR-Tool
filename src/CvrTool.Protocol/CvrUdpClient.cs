// Protocol / Explicit CVR connection
// Module: Encodes CVR messages, reassembles replies and manages UDP exchanges.
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
    private readonly bool targetSnapshotAck;
    public CvrUdpClient(NetworkTarget target)
    {
        target = NetworkPreparation.Validate(target).Target;
        timeoutMs = target.TimeoutMs;
        targetSnapshotAck = target.ReadOnlySnapshot;
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

    // DSP-1004 returns data status and uses segment 10 for these block reads.
    public byte[] ReadInspection(byte function, byte channel, byte segment, byte side)
    {
        if (function is not (43 or 50 or 51 or 52 or 53 or 54 or 55 or 57 or 62 or 91))
            return Read(function, channel, segment, side);
        byte replySegment = function is 43 or 50 or 51 or 54 or 55 or 57 or 91 ? (byte)10 : segment;
        return Exchange(function, channel, segment, side, [], 2, 1, replySegment: replySegment).GetAwaiter().GetResult();
    }

    public byte[] ReadAllocation() =>
        Exchange(81, 0, 0, 0, [], 2, 0, controllerTable: true).GetAwaiter().GetResult();

    public byte[] ReadPresetNames() =>
        Exchange(59, 0, 0, 0, new byte[34], 2, 0, controllerTable: true).GetAwaiter().GetResult();

    public byte[] ReadSnapshot(byte function)
    {
        if (function is not (0 or 6 or 27 or 71 or 83)) throw new ArgumentException("Only observed bulk reads are supported.");
        return Exchange(function, 0, 0, 0, [], 2, 1, observedBulk: true).GetAwaiter().GetResult();
    }

    private void ObserveIdentity(CvrMessage message)
    {
        if (message is { Function: 0, Status: 1, Channel: 3, Segment: 10, Side: 0 } &&
            message.Body.Length is 75 or 79)
            Volatile.Write(ref identityBody, message.Body.ToArray());
    }

    // DSP-1004 returns a full settings block immediately after accepting reset.
    public byte[] ResetSnapshot() => Exchange(16, 0, 0, 0, [1], 0, 1, observedBulk: true, replyFunction: 27).GetAwaiter().GetResult();

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
                        if(header.Ack && (response.AsSpan(4,4).SequenceEqual(packet.AsSpan(4,4)) || targetSnapshotAck && header.Count == 0))break;
                        if(!header.Ack){var ack=response[..10];ack[8]=1;socket.Send(ack,ack.Length);}
                    }
                    catch(InvalidDataException) { }
                }
            }
        }
        finally { serial.Release(); }
    }
    private async Task<byte[]> Exchange(byte function, byte channel, byte segment, byte side, byte[] body, byte sendStatus, byte responseStatus, bool observedBulk = false, bool controllerTable = false, byte? replySegment = null, byte? replyFunction = null)
    {
        // All backend operations hold their own gate; the transport also serializes standalone callers.
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            for (var drained = 0; drained < 64 && socket.Available > 0; drained++)
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
            var assembly = new CvrReplyAssembler();
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
                var message = assembly.Accept(packet);
                if (message is null) continue;
                ObserveIdentity(message);
                if (controllerTable)
                {
                    bool echoed = message.Status == 0 && message.Channel == 0 && message.Segment == 0;
                    bool bulk = message.Status == 1 && message.Channel == 32 && message.Segment == 10;
                    bool validSize = function == 81 ? message.Body.Length == 40 : message.Body.Length is 512 or 768 or 1280;
                    if (message.Function != function || message.Side != 0 || !(echoed || bulk) ||
                        !validSize) continue;
                }
                else if (observedBulk)
                {
                    var expectedFunction = replyFunction ?? function;
                    var validLength = expectedFunction == 83 ? message.Body.Length == 1 : function == 71 ? message.Body.Length == 93 : function == 0 ? message.Body.Length is 75 or 79 :
                        expectedFunction == 27 ? message.Body.Length is 2232 or 2687 :
                        message.Body.Length is 32 or 56 or 72 or 76 or 88 or 92 or 96;
                    var responseChannel = function == 0 ? 3 : 32;
                    if (message.Function != expectedFunction || message.Status != 1 || message.Channel != responseChannel ||
                        message.Segment != 10 || message.Side != 0 || !validLength) continue;
                    if (expectedFunction == 27)
                    {
                        // Read completion only: no settings payload and no status-1 write.
                        var confirmation = CvrWire.Encode(new(27, 3, 0, 0, 0, []));
                        await socket.SendAsync(confirmation).ConfigureAwait(false);
                    }
                }
                else if (message.Function != function || message.Status != responseStatus || message.Channel != channel || message.Segment != (replySegment ?? segment) || message.Side != side)
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
