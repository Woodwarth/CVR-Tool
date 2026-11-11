using System.Net;
using System.Net.Sockets;
using System.Text;
using CvrTool.Application;
using CvrTool.Protocol;

var count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
void Reject<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T) { count++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}
async Task RejectAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T) { count++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}
var target = new NetworkTarget(" Offline target ", "192.0.2.10", LocalAddress: "192.0.2.20");
var plan = NetworkPreparation.Validate(target);
Check(plan.HardwareAccessAllowed && !plan.IsLoopback && plan.Target.Name == "Offline target", "LAN target is a normalized offline plan");
Check(ProtocolStatus.HardwareAccessEnabled && !ProtocolStatus.PhysicalAmplifierValidated, "Manual hardware path is available with physical validation explicitly pending");
Reject<InvalidOperationException>(() => new LoopbackUdpTransport(target));
foreach (var address in new[] { "", "localhost", "127.1", "2130706433", "127.000.0.1", "999.1.1.1", "0.0.0.0", "255.255.255.255", "239.1.2.3", "192.0.2.255", "::1" })
    Reject<ArgumentException>(() => NetworkPreparation.Validate(target with { RemoteAddress = address }));
Reject<ArgumentException>(() => NetworkPreparation.Validate(target with { RemoteAddress = "127.0.0.1" }));
Reject<ArgumentException>(() => NetworkPreparation.Validate(target with { Name = "" }));
Reject<ArgumentOutOfRangeException>(() => NetworkPreparation.Validate(target with { RemotePort = 0 }));
Reject<ArgumentOutOfRangeException>(() => NetworkPreparation.Validate(target with { LocalPort = -1 }));
Reject<ArgumentOutOfRangeException>(() => NetworkPreparation.Validate(target with { TimeoutMs = 0 }));

byte[] DataFrame(int length, byte function, byte operation)
{
    var bytes = new byte[length];
    bytes[0] = 3;
    bytes[1] = 0xD9;
    bytes[6] = 0;
    bytes[7] = 1;
    bytes[10] = 0x55;
    bytes[11] = function;
    bytes[12] = operation;
    return bytes;
}
void Checksum(byte[] bytes) { bytes[^1] = (byte)(bytes[10..^1].Sum(b => (int)b) & 255); }
var query = DataFrame(23, 6, 2);
Checksum(query);
var inspection = CapturedFrameInspector.Inspect(query);
Check(inspection is { Kind: CapturedFrameKind.ObservedSingleDatagram, ChecksumMatches: true, Function: 6, Operation: 2 }, "Synthetic observed-shape query recognized");
query[15] ^= 1;
Check(CapturedFrameInspector.Inspect(query).ChecksumMatches == false, "Corrupt body checksum detected");
Check(CapturedFrameInspector.Inspect(Convert.FromHexString("03d9ca00010000010121")).Kind == CapturedFrameKind.AckCandidate, "ACK stays a candidate, not a successful state change");
Check(CapturedFrameInspector.Inspect(new byte[9]).Kind == CapturedFrameKind.Unknown, "Truncated packet rejected");
var fragment = new byte[460];
fragment[0] = 3;
fragment[1] = 0xD9;
fragment[6] = 1;
Check(CapturedFrameInspector.Inspect(fragment).Kind == CapturedFrameKind.FragmentCandidate, "Fragments remain undecoded");
var response = DataFrame(102, 0, 1);
Encoding.ASCII.GetBytes("SYNTHETIC-DSP-2002").CopyTo(response, 20);
Checksum(response);
Check(CapturedFrameInspector.Inspect(response).ModelHint == "DSP-2002", "Model text hint from synthetic body");
response[^1] ^= 1;
Check(CapturedFrameInspector.Inspect(response).ModelHint is null, "Corrupt frame cannot supply a model hint");

// Actual UDP tests use only OS loopback, random free ports and synthetic non-CVR messages.
using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
var serverPort = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
var loopback = new NetworkTarget("Loopback", "127.0.0.1", serverPort, "127.0.0.1", 0, 2000);
using var transport = new LoopbackUdpTransport(loopback);
using var testDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var token = testDeadline.Token;
var echo = Task.Run(async () =>
{
    for (var i = 0; i < 3; i++)
    {
        var request = await server.ReceiveAsync(token);
        using var stranger = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await stranger.SendAsync("WRONG-PEER"u8.ToArray(), request.RemoteEndPoint, token);
        await server.SendAsync(request.Buffer, request.RemoteEndPoint, token);
    }
}, token);
var one = transport.ExchangeAsync("FIRST"u8.ToArray(), token);
var two = transport.ExchangeAsync("SECOND"u8.ToArray(), token);
Check(Encoding.ASCII.GetString(await one) == "FIRST" && Encoding.ASCII.GetString(await two) == "SECOND", "Serialized loopback exchanges filter other peers");
Check(Encoding.ASCII.GetString(await transport.ExchangeAsync("THIRD"u8.ToArray(), token)) == "THIRD", "Transport remains reusable");
await echo;
await RejectAsync<ArgumentOutOfRangeException>(() => transport.ExchangeAsync(new byte[4097], token));
await RejectAsync<ArgumentOutOfRangeException>(() => transport.ExchangeAsync(Array.Empty<byte>(), token));
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    await RejectAsync<OperationCanceledException>(() => transport.ExchangeAsync(new byte[] { 1 }, canceled.Token));
}
using var silentServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var timeoutTransport = new LoopbackUdpTransport(loopback with { RemotePort = ((IPEndPoint)silentServer.Client.LocalEndPoint!).Port, TimeoutMs = 100 });
await RejectAsync<TimeoutException>(() => timeoutTransport.ExchangeAsync("TIMEOUT"u8.ToArray(), token));
using var cancellationServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var cancellationTransport = new LoopbackUdpTransport(loopback with { RemotePort = ((IPEndPoint)cancellationServer.Client.LocalEndPoint!).Port });
using (var inFlightCancellation = new CancellationTokenSource())
{
    var inFlight = cancellationTransport.ExchangeAsync("CANCEL"u8.ToArray(), inFlightCancellation.Token);
    await cancellationServer.ReceiveAsync(token);
    inFlightCancellation.Cancel();
    await RejectAsync<OperationCanceledException>(() => inFlight);
}
var disposeInFlight = cancellationTransport.ExchangeAsync("DISPOSE"u8.ToArray(), token);
await cancellationServer.ReceiveAsync(token);
cancellationTransport.Dispose();
await RejectAsync<OperationCanceledException>(() => disposeInFlight);
using var oversizedServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var oversizedTransport = new LoopbackUdpTransport(loopback with { RemotePort = ((IPEndPoint)oversizedServer.Client.LocalEndPoint!).Port });
var oversizedReply = Task.Run(async () => { var request = await oversizedServer.ReceiveAsync(token); await oversizedServer.SendAsync(new byte[4097], request.RemoteEndPoint, token); }, token);
await RejectAsync<InvalidDataException>(() => oversizedTransport.ExchangeAsync(new byte[] { 1 }, token));
await oversizedReply;
transport.Dispose();
await RejectAsync<ObjectDisposedException>(() => transport.ExchangeAsync(new byte[] { 1 }, token));

var directory = Path.Combine(Path.GetTempPath(), "cvr-network-checks-" + Guid.NewGuid().ToString("N"));
var configPath = Path.Combine(directory, "network.json");
var settings = new NetworkSetupService(configPath);
try
{
    Check(await settings.LoadAsync(token) is null && !Directory.Exists(directory), "Missing config does not create files");
    await settings.SaveAsync(target, token);
    var loaded = await settings.LoadAsync(token);
    Check(loaded is { HardwareAccessAllowed: true } && loaded.Target.RemoteAddress == target.RemoteAddress, "Atomic config round-trip preserves an explicitly connectable target");
    var original = await File.ReadAllTextAsync(configPath, token);
    await RejectAsync<ArgumentException>(() => settings.SaveAsync(target with { RemoteAddress = "localhost" }, token));
    Check(await File.ReadAllTextAsync(configPath, token) == original, "Invalid config does not replace existing settings");
    await File.WriteAllTextAsync(configPath, "{\"SchemaVersion\":2,\"Target\":null}", token);
    await RejectAsync<InvalidDataException>(async () => await settings.LoadAsync(token));
    await File.WriteAllTextAsync(configPath, "not json", token);
    await RejectAsync<InvalidDataException>(async () => await settings.LoadAsync(token));
    await File.WriteAllTextAsync(configPath, original.TrimEnd()[..^1] + ",\"Unexpected\":true}", token);
    await RejectAsync<InvalidDataException>(async () => await settings.LoadAsync(token));
    await File.WriteAllTextAsync(configPath, new string(' ', 16385), token);
    await RejectAsync<InvalidDataException>(async () => await settings.LoadAsync(token));
}
finally
{
    // Delete only the exact files created by this test, no recursive directory operations.
    if (File.Exists(configPath))
        File.Delete(configPath);
    if (Directory.Exists(directory))
        Directory.Delete(directory);
}
CvrPeerChecks.Run(Check);
SnapshotPeerChecks.Run(Check);
var sessionLabel="stress-"+Guid.NewGuid();
using (var sessionHost = new SessionHost(new DemoBackend(), sessionLabel))
{
    var clientA=new SessionBackend(sessionLabel);var clientB=new SessionBackend(sessionLabel);
    for(var request=0;request<200;request++)
        if((request%2==0 ? clientA : clientB).GetDevice("demo-1004").Outputs.Count!=4)throw new Exception("Sequential IPC state mismatch");
    Check(true,"200 alternating IPC connections reuse the listening endpoint");
    using(var interrupted=new System.IO.Pipes.NamedPipeClientStream(".",SessionDefaults.Resolve(sessionLabel),System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous))
        await interrupted.ConnectAsync(5000);
    Check((await Task.Run(()=>clientA.ListDevices()).WaitAsync(TimeSpan.FromSeconds(10))).Count==3,"An aborted IPC peer does not stop the session listener");
    await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>
    {
        var client=new SessionBackend(sessionLabel);
        for(var request=0;request<50;request++)
            if(client.GetInputProcessing("demo-2002",1).State.HighPass is null)throw new Exception("Concurrent IPC state mismatch");
    })));
    Check(true,"200 concurrent IPC reads retain complete crossover snapshots");
}

Console.WriteLine($"PASS: {count} network checks (synthetic loopback peers; no physical amplifier)");
