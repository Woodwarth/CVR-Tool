// Application / Automatic CVR discovery
// Purpose: list supported devices from CVR identity broadcasts on the wired amp LAN.
// Units: one bounded search per wired IPv4 interface every five seconds.
// State: discovery shares local-port ownership with read-only snapshot polling.
// Scope: identity queries only; no IP-range probes, settings writes or firmware work.
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed record DiscoveryInterface(string Address, string Broadcast, int PrefixLength);

public sealed class CvrDiscoveryService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    public string? LastError { get; private set; }
    public CvrDiscoveryService(Action<NetworkTarget> found)
    {
        worker = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (var adapter in Interfaces())
                    {
                        if (stop.IsCancellationRequested) break;
                        try
                        {
                            foreach (var target in Search(adapter))
                            {
                                if (stop.IsCancellationRequested) break;
                                try { found(target); } catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException or InvalidOperationException or SocketException) { LastError = ex.Message; }
                            }
                        }
                        catch (Exception ex) when (ex is IOException or SocketException) { LastError = ex.Message; }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    public static IReadOnlyList<DiscoveryInterface> Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => new DiscoveryInterface(a.Address.ToString(), Broadcast(a.Address, a.PrefixLength), a.PrefixLength))).ToArray();
    private static string Broadcast(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < 32; bit++) bytes[bit / 8] |= (byte)(128 >> (bit % 8));
        return new IPAddress(bytes).ToString();
    }
    public static bool SupportedIdentity(CvrMessage message)
    {
        if (message is not { Function: 0, Status: 1, Channel: 3, Segment: 10, Side: 0 } || message.Body.Length is not (75 or 79)) return false;
        var body = message.Body;
        var version = Encoding.ASCII.GetString(body, 9, 6);
        return body[5] == 'B' && body[2] is (byte)'2' or (byte)'4' && body[4] == body[2] &&
            body[71] == body[73] && body[73] == body[2] - '0' && version is "106118" or "106119";
    }
    private static bool SameSubnet(IPAddress remote, DiscoveryInterface adapter)
    {
        var a = remote.GetAddressBytes(); var b = IPAddress.Parse(adapter.Address).GetAddressBytes();
        if (a.Length != 4 || adapter.PrefixLength is < 0 or > 32) return false;
        for (var bit = 0; bit < adapter.PrefixLength; bit++) if ((a[bit / 8] & (128 >> (bit % 8))) != (b[bit / 8] & (128 >> (bit % 8)))) return false;
        return true;
    }
    public static IReadOnlyList<NetworkTarget> Search(DiscoveryInterface adapter, int localPort = 45454, int remotePort = 45455, int durationMs = 300)
    {
        return CvrEndpointGate.Run(adapter.Address, localPort, () =>
        {
            using var socket = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = true, EnableBroadcast = true };
            socket.Client.Bind(new IPEndPoint(IPAddress.Parse(adapter.Address), localPort));
            var query = CvrWire.Encode(new(0, 2, 0, 0, 0, []));
            socket.Send(query, query.Length, new IPEndPoint(IPAddress.Parse(adapter.Broadcast), remotePort));
            var found = new Dictionary<string, NetworkTarget>();
            using var deadline = new CancellationTokenSource(durationMs);
            while (!deadline.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try { received = socket.ReceiveAsync(deadline.Token).AsTask().GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { break; }
                if (received.RemoteEndPoint.Port != remotePort || !SameSubnet(received.RemoteEndPoint.Address, adapter)) continue;
                try
                {
                    var header = CvrWire.Header(received.Buffer);
                    if (header.Ack || header.Count != 1) continue;
                    var message = CvrWire.DecodeInner(received.Buffer[10..]);
                    if (!SupportedIdentity(message)) continue;
                    var ack = received.Buffer[..10]; ack[8] = 1; socket.Send(ack, ack.Length, received.RemoteEndPoint);
                    var address = received.RemoteEndPoint.Address.ToString();
                    found[address] = NetworkPreparation.Validate(new("Discovered CVR amplifier", address, remotePort, adapter.Address, localPort, 1500, true)).Target;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException) { }
            }
            return (IReadOnlyList<NetworkTarget>)found.Values.ToArray();
        });
    }
    public void Dispose()
    {
        stop.Cancel();
        worker.GetAwaiter().GetResult();
        stop.Dispose();
    }
}
