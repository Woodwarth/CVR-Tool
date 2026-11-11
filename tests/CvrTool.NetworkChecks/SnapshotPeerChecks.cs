// Checks / Observed bulk-read profile
// Purpose: exercise read-only connections beside demos with captured reply shapes.
// Units: 2687-byte native settings and 92-byte four-slot telemetry.
// State: synthetic peers record all requests, confirmations and forbidden writes.
// Scope: loopback only; no real device addresses or private capture bytes.
using System.Net;
using System.Net.Sockets;
using System.Text;
using CvrTool.Application;
using CvrTool.Core;
using CvrTool.Protocol;

static class SnapshotPeerChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var peer = new Peer();
        using var portReservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var sharedPort = ((IPEndPoint)portReservation.Client.LocalEndPoint!).Port;
        portReservation.Dispose();
        using var backend = new ConnectionBackend();
        var service = new AmplifierService(backend);
        var target = new NetworkTarget("Live fixture", "127.0.0.1", peer.Port, "127.0.0.1", sharedPort, 500, true);
        var device = service.Connect(target);
        var id = device.Id;
        check(device is { IsDemo: false, Name: "Live fixture", Readings.ReadOnly: true } && device.Capabilities.Model == "DSP-1004", "Observed bulk profile connects a real-shaped device without replacing demos");
        check(service.ListDevices().Count(d => d.IsDemo) == 3 && service.ListDevices().Any(d => d.Id == id), "Live and all demo devices coexist");
        check(device.Readings!.MacAddress == "02:00:00:00:00:01" && device.Readings.FirmwareHeader.Contains("106119"), "Validated identity announcements populate metadata");
        check(service.GetInputProcessing(id, 1).GainDb == 0 && service.GetRouting(id).Cells.Count == 16, "Inputs and full matrix come from the native snapshot");
        var telemetry = service.GetTelemetry(id);
        check(telemetry[0] is { TemperatureC: 42, Voltage: 40, Current: 5, LoadOhms: 8, LevelDbFs: null } && telemetry[0].Status!.Clip == true, "Observed telemetry displays known measurements without inventing dBFS");
        check(device.Readings.Power == 25 && device.Readings.StatusCodes![0] == 5, "Power value and raw status provenance remain available");
        check(!service.GetFir(id, 1).CoefficientsKnown && service.GetFir(id, 1).Coefficients.Count == 0, "Missing FIR coefficients are explicitly unknown");
        check(service.GetDevicePresets(id).Slots.Count == 0, "Unobserved slot names are not fabricated");
        check(service.ExportNativePreset(id).SequenceEqual(peer.Sync), "Known native settings can be backed up exactly without synthesized FIR");
        try { service.Apply(service.Plan(new(id, 1, Muted: true)).Id); throw new Exception("Read-only mutation accepted"); }
        catch (NotSupportedException) { check(true, "Read-only connection rejects settings changes before any write"); }
        var pipe = "cvr-snapshot-" + Guid.NewGuid().ToString("N");
        using var host = new SessionHost(backend, pipe);
        var remote = new AmplifierService(new SessionBackend(pipe));
        check(remote.GetDevice(id).Readings!.ReadOnly && remote.GetInputProcessing(id, 1).Bands.Count == 8, "Shared IPC exposes the same live snapshot and read-only mode");
        check(SpinWait.SpinUntil(() => peer.Confirmations > 0, 1000) && peer.Writes == 0 && peer.UnexpectedQueries == 0, "Only bulk reads, transport ACKs and empty status-3 confirmation are sent");
        check(service.GetDevice(id).Readings is { CanSetDisplayLock: true, CanSetStandby: true }, "Live mode advertises the two verified operational controls");
        check(service.SetControlState(id, displayLocked: true) == new DeviceControlState(true, false), "Live display lock is confirmed through fresh full settings");
        check(service.SetControlState(id, standby: true) == new DeviceControlState(true, true), "Live standby status-zero command is confirmed through full settings");
        check(remote.SetControlState(id, displayLocked: false, standby: false) == new DeviceControlState(false, false), "Shared IPC restores both operational controls with readback");
        var writesAfterRestore = peer.Writes;
        service.SetControlState(id, displayLocked: false, standby: false);
        check(peer.Writes == writesAfterRestore, "Already-observed control state does not send another write");
        peer.IgnoreControlWrites = true;
        try { service.SetControlState(id, displayLocked: true); throw new Exception("Rejected control write accepted"); }
        catch (InvalidOperationException) { check(true, "Control transport delivery without matching full readback fails"); }
        finally { peer.IgnoreControlWrites = false; }
        check(!service.GetControlState(id).DisplayLocked, "Rejected control write retains the independently observed state");
        check(peer.DelayedStandbyReads > 0, "Standby readback waits for asynchronous native state without repeating writes");
        using (var secondPeer = new Peer("demo-1502"))
        {
            var second = service.Connect(target with { RemotePort = secondPeer.Port });
            check(second.Outputs.Count == 2 && service.GetInputProcessing(second.Id, 2).Bands.Count == 8, "Two-channel live snapshot has the same processing functions");
            check(service.ListDevices().Count(d => !d.IsDemo) == 2, "Two live devices share the same local UDP port beside demos");
            service.Disconnect(second.Id);
        }
        var discovered = CvrDiscoveryService.Search(new("127.0.0.1", "127.0.0.1", 8), localPort: 0, remotePort: peer.Port, durationMs: 150);
        check(discovered.Count == 1 && discovered[0].ReadOnlySnapshot && discovered[0].RemotePort == peer.Port, "Bounded identity discovery finds the synthetic peer without adding an address");
        var identity = new byte[79]; peer.Sync[..32].CopyTo(identity, 0); identity[71] = identity[73] = 4;
        check(CvrDiscoveryService.SupportedIdentity(new(0, 1, 3, 10, 0, identity)), "Discovery accepts the observed identity/topology profile");
        identity[73] = 2;
        check(!CvrDiscoveryService.SupportedIdentity(new(0, 1, 3, 10, 0, identity)), "Discovery rejects inconsistent topology");
        check(!CvrDiscoveryService.SupportedIdentity(new(0, 1, 0, 0, 0, new byte[79])), "Discovery does not accept unrelated reply headers");
        check(service.Disconnect(id).Disconnected && service.ListDevices().All(d => d.IsDemo), "Disconnect preserves demos and disposes the live owner");
        using var client = new CvrUdpClient(target);
        check(client.ReadSnapshot(6).Length == 92, "Bulk matcher ignores unrelated/wrong-address status messages");
        try { client.Read(6); throw new Exception("Bulk reply relaxed legacy matching"); }
        catch (TimeoutException) { check(true, "Legacy echo/status matching remains strict"); }
        try { client.ReadSnapshot(32); throw new Exception("Unknown bulk query sent"); }
        catch (ArgumentException) { check(true, "Bulk API cannot send unevidenced individual queries"); }
    }

    private sealed class Peer : IDisposable
    {
        private readonly UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource stop = new();
        private readonly Task listener;
        public byte[] Sync { get; }
        public int Port => ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        public int Writes, Confirmations, UnexpectedQueries;
        public volatile bool IgnoreControlWrites;
        private bool standby;
        private long nativeStandbyDue;
        private byte nativeStandbyValue;
        public int DelayedStandbyReads;
        public Peer(string model = "demo-1004")
        {
            var service = new AmplifierService(new DemoBackend());
            var file = service.ExportNativePreset(model);
            Sync = file[..NativePresetCodec.Inspect(file).SyncLength];
            listener = Task.Run(Listen);
        }
        private async Task Send(CvrMessage message, IPEndPoint endpoint)
        {
            foreach (var packet in CvrWire.EncodeFragments(message))
                await socket.SendAsync(packet, endpoint, stop.Token);
        }
        private async Task Listen()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(stop.Token);
                    var header = CvrWire.Header(received.Buffer);
                    if (header.Ack) continue;
                    var message = CvrWire.DecodeInner(received.Buffer[10..]);
                    if ((message.Function == 17 && message.Status == 1) || (message.Function == 15 && message.Status == 0))
                    {
                        Interlocked.Increment(ref Writes);
                        if (!IgnoreControlWrites)
                        {
                            if (message.Function == 17) Sync[33] = message.Body[0];
                            else { standby = message.Body[0] == 1; nativeStandbyValue = message.Body[0]; nativeStandbyDue = Environment.TickCount64 + 150; }
                        }
                        continue;
                    }
                    if (message.Status == 1) { Interlocked.Increment(ref Writes); continue; }
                    if (message is { Function: 27, Status: 3, Body.Length: 0 })
                    { Interlocked.Increment(ref Confirmations); continue; }
                    if (message.Status != 2 || message.Function is not (0 or 6 or 27))
                    { Interlocked.Increment(ref UnexpectedQueries); continue; }
                    await socket.SendAsync(Convert.FromHexString("03D9000000C201010100"), received.RemoteEndPoint, stop.Token);
                    if (message.Function is 0 or 27)
                    {
                        var identity = new byte[79]; Sync[..32].CopyTo(identity, 0);
                        Encoding.ASCII.GetBytes("Live fixture").CopyTo(identity, 32);
                        new byte[] { 2, 0, 0, 0, 0, 1 }.CopyTo(identity, 64);
                        identity[71] = identity[73] = (byte)(Sync[2] - (byte)'0');
                        await Send(new(0, 1, 3, 10, 0, identity), received.RemoteEndPoint);
                        if (message.Function == 27)
                        {
                            if (nativeStandbyDue != 0)
                            {
                                if (Environment.TickCount64 >= nativeStandbyDue) { Sync[32] = nativeStandbyValue; nativeStandbyDue = 0; }
                                else Interlocked.Increment(ref DelayedStandbyReads);
                            }
                            await Send(new(27, 1, 32, 10, 0, Sync), received.RemoteEndPoint);
                        }
                    }
                    else
                    {
                        var data = new byte[92];
                        for (var i = 0; i < 4; i++)
                        {
                            BitConverter.GetBytes(42f + i).CopyTo(data, i * 4);
                            BitConverter.GetBytes(40f).CopyTo(data, 20 + i * 4);
                            BitConverter.GetBytes(5f).CopyTo(data, 36 + i * 4);
                            data[52 + i] = standby ? (byte)1 : (byte)5;
                        }
                        BitConverter.GetBytes(25f).CopyTo(data, 16);
                        await Send(new(6, 1, 0, 0, 0, new byte[92]), received.RemoteEndPoint);
                        await Send(new(6, 1, 32, 10, 1, data), received.RemoteEndPoint);
                        await Send(new(6, 1, 32, 10, 0, new byte[91]), received.RemoteEndPoint);
                        await Send(new(6, 1, 32, 10, 0, data), received.RemoteEndPoint);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
        public void Dispose() { stop.Cancel(); listener.GetAwaiter().GetResult(); socket.Dispose(); stop.Dispose(); }
    }
}
