// Checks / UDP fault recovery
// Purpose: exercise corrupt replies, interleaving and bounded timeout recovery.
// Units: real loopback datagrams carrying synthetic CVR settings payloads.
// State: each scenario owns one ephemeral peer and one client; no physical traffic.
// Scope: reads are never retransmitted and no setting commands are issued.
using System.Net;
using System.Net.Sockets;
using CvrTool.Protocol;
using CvrTool.Application;

static class DatagramFaultChecks
{
    public static void Run(Action<bool, string> check)
    {
        var data = Enumerable.Range(0, 2687).Select(i => (byte)(i % 251)).ToArray();
        var fragments = CvrWire.EncodeFragments(new(27, 1, 32, 10, 0, data));
        var identity = CvrWire.Encode(new(0, 1, 3, 10, 0, new byte[79]));
        WithPeer(async (peer, endpoint, index, token) =>
        {
            var corrupt = CvrWire.Encode(new(83, 1, 32, 10, 0, [0])); corrupt[^1] ^= 1;
            await peer.SendAsync(corrupt, endpoint, token);
            await peer.SendAsync(CvrWire.Encode(new(83, 1, 32, 10, 0, [1])), endpoint, token);
        }, client => check(client.ReadSnapshot(83).SequenceEqual(new byte[] { 1 }), "Corrupt inner checksum is ignored while a valid reply still completes the read"));
        WithPeer(async (peer, endpoint, index, token) =>
        {
            await peer.SendAsync(fragments[0], endpoint, token);
            await peer.SendAsync(identity, endpoint, token);
            foreach (var packet in fragments.Skip(1)) await peer.SendAsync(packet, endpoint, token);
        }, client =>
        {
            check(client.ReadSnapshot(27).SequenceEqual(data), "Identity announcements do not erase an incomplete settings reply");
            check(client.ObservedIdentity?.Length == 79, "Interleaved valid identity metadata is retained");
        });
        WithPeer(async (peer, endpoint, index, token) =>
        {
            foreach (var packet in fragments.Reverse())
            { await peer.SendAsync(packet, endpoint, token); await peer.SendAsync(packet, endpoint, token); }
        }, client => check(client.ReadSnapshot(27).SequenceEqual(data), "Reordered and identical duplicate fragments assemble successfully"));
        WithPeer(async (peer, endpoint, index, token) =>
        {
            await peer.SendAsync(fragments[0], endpoint, token);
            var conflicting = fragments[0].ToArray(); conflicting[25] ^= 1;
            await peer.SendAsync(conflicting, endpoint, token);
            foreach (var packet in fragments) await peer.SendAsync(packet, endpoint, token);
        }, client => check(client.ReadSnapshot(27).SequenceEqual(data), "Conflicting duplicate fragments cannot poison a subsequent complete reply"));
        WithPeer(async (peer, endpoint, index, token) =>
        {
            for (var size = 700; size < 705; size++)
                await peer.SendAsync(CvrWire.EncodeFragments(new(52, 0, 0, 0, 0, new byte[size]))[0], endpoint, token);
            foreach (var packet in fragments) await peer.SendAsync(packet, endpoint, token);
        }, client => check(client.ReadSnapshot(27).SequenceEqual(data), "Bounded assembly storage admits a valid reply after unrelated incomplete messages"));
        WithPeer(async (peer, endpoint, index, token) =>
        {
            if (index == 0) { await peer.SendAsync(fragments[0], endpoint, token); return; }
            foreach (var packet in fragments) await peer.SendAsync(packet, endpoint, token);
        }, client =>
        {
            try { client.ReadSnapshot(27); throw new Exception("Incomplete response accepted"); }
            catch (TimeoutException) { check(true, "Incomplete fragments time out without silently retrying the request"); }
            check(client.ReadSnapshot(27).SequenceEqual(data), "A fresh explicit query succeeds after the prior query timed out");
        });
    }
    public static void CheckSlowConnection(Action<bool, string> check)
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var backend = new ConnectionBackend();
        var attempt = Task.Run(() =>
        {
            try { backend.Connect(new("Silent peer", "127.0.0.1", ((IPEndPoint)silent.Client.LocalEndPoint!).Port, "127.0.0.1", 0, 1500)); }
            catch (TimeoutException) { }
        });
        using var deadline = new CancellationTokenSource(5000);
        silent.ReceiveAsync(deadline.Token).AsTask().GetAwaiter().GetResult();
        var cached = Task.Run(() => backend.ListDevices());
        var responsive = cached.Wait(TimeSpan.FromMilliseconds(500));
        attempt.GetAwaiter().GetResult();
        check(responsive && cached.Result.Count == 1, "Slow connection handshakes do not block cached reads and demo navigation");
    }
    private static void WithPeer(Func<UdpClient, IPEndPoint, int, CancellationToken, Task> reply, Action<CvrUdpClient> run)
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var stop = new CancellationTokenSource();
        var listener = Task.Run(async () =>
        {
            var index = 0;
            try
            {
                while (true)
                {
                    var request = await peer.ReceiveAsync(stop.Token);
                    if (CvrWire.Header(request.Buffer).Ack) continue;
                    if (CvrWire.DecodeInner(request.Buffer[10..]).Status != 2) continue;
                    await reply(peer, request.RemoteEndPoint, index++, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        try
        {
            using var client = new CvrUdpClient(new("Fault peer", "127.0.0.1", ((IPEndPoint)peer.Client.LocalEndPoint!).Port, "127.0.0.1", 0, 300));
            run(client);
        }
        finally { stop.Cancel(); listener.GetAwaiter().GetResult(); }
    }
}
