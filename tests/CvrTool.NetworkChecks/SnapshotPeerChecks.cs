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
        check(service.ListDevices().Count(d => d.IsDemo) == 1 && service.ListDevices().Any(d => d.Id == id), "Live and all demo devices coexist");
        check(device.Readings!.MacAddress == "02:00:00:00:00:01" && device.Readings.FirmwareHeader.Contains("106119"), "Validated identity announcements populate metadata");
        check(service.GetInputProcessing(id, 1).GainDb == 0 && service.GetRouting(id).Cells.Count == 16, "Inputs and full matrix come from the native snapshot");
        var telemetry = service.GetTelemetry(id);
        check(telemetry[0] is { TemperatureC: 42, Voltage: 40, Current: 5, LoadOhms: 8, LevelDbFs: null } && telemetry[0].Status!.Clip == true, "Observed telemetry displays known measurements without inventing dBFS");
        check(Math.Abs(telemetry[0].InputMeterDb!.Value + 20) < .001 && telemetry[0].InputClip == false && telemetry[1].InputMeterDb == -40 && telemetry[1].InputClip == true, "Independent input signal and active-low clip follow each channel");
        check(telemetry[0].OutputMeterDb == Math.Clamp(20 * Math.Log10(40 / Math.Max(1, service.GetOutputDynamics(id,1).Rms.MaximumVolts)), -40, 0), "Output meter references the channel RMS maximum");
        check(device.Readings.Power == 25 && device.Readings.StatusCodes![0] == 5, "Power value and raw status provenance remain available");
        check(service.GetFir(id, 1) is { CoefficientsKnown: true, Coefficients.Count: 512 } taps && taps.Coefficients[0] == 0.5, "Observed FIR reply populates the live output");
        check(service.GetNativePresetMetadata(id)?.Allocation?[0].LoadOhms == 8, "Allocation reads retain configured output load");
        check(service.GetDevicePresets(id).Slots is { Count: 24 } slots && slots[23].Name == "Stage", "Live preset names follow the received slot count");
        check(service.ExportNativePreset(id).SequenceEqual(peer.Sync), "Known native settings can be backed up exactly without synthesized FIR");
        var pipe = "cvr-snapshot-" + Guid.NewGuid().ToString("N");
        using var host = new SessionHost(backend, pipe);
        var remote = new AmplifierService(new SessionBackend(pipe));
        check(remote.GetDevice(id).Readings!.ReadOnly && remote.GetInputProcessing(id, 1).Bands.Count == 8, "Shared IPC exposes the same live snapshot and read-only mode");
        check(remote.InspectProtocol(id,59,1,0).Contains("24 entries") && remote.InspectProtocol(id,16,1,0).Contains("protected"), "IPC inspection reads the preset table and never issues reset");
        var limiterReply = remote.InspectProtocol(id, 91, 3, 0);
        check(limiterReply.Contains("6 Ω") && limiterReply.Contains("match last full settings"), "Output C limiter load is decoded from the combined read through IPC");
        check(SpinWait.SpinUntil(() => peer.Confirmations > 0, 1000) && peer.Writes == 0 && peer.UnexpectedQueries == 0, "Only expected reads and confirmations are sent");
        var oldEq = service.GetOutputProcessing(id, 1);
        var editedEq = service.ApplyOutputProcessing(service.PlanOutputProcessing(new(id, 1, EqEnabled: !oldEq.EqEnabled)).Id);
        check(editedEq.EqEnabled != oldEq.EqEnabled, "Output EQ block edits are independently read back");
        service.ApplyOutputProcessing(service.PlanOutputProcessing(new(id, 1, EqEnabled: oldEq.EqEnabled)).Id);
        check(service.GetDevice(id).Readings is { CanSetDisplayLock: true, CanSetStandby: true }, "Live mode advertises the two verified operational controls");
        check(service.SetControlState(id, displayLocked: true) == new DeviceControlState(true, false, Wink: false), "Live display lock is confirmed through fresh full settings");
        check(service.SetControlState(id, standby: true) == new DeviceControlState(true, true, Wink: false), "Live standby status-zero command is confirmed through full settings");
        check(remote.SetControlState(id, displayLocked: false, standby: false) == new DeviceControlState(false, false, Wink: false), "Shared IPC restores both operational controls with readback");
        var writesAfterRestore = peer.Writes;
        service.SetControlState(id, displayLocked: false, standby: false);
        check(peer.Writes == writesAfterRestore, "Already-observed control state does not send another write");
        peer.IgnoreControlWrites = true;
        try { service.SetControlState(id, displayLocked: true); throw new Exception("Rejected control write accepted"); }
        catch (InvalidOperationException) { check(true, "Control transport delivery without matching full readback fails"); }
        finally { peer.IgnoreControlWrites = false; }
        check(!service.GetControlState(id).DisplayLocked, "Rejected control write retains the independently observed state");
        check(device.Readings!.OperatingMinutes == 26259 && DeviceMetadataCodec.FormatOperatingTime(device.Readings.OperatingMinutes) == "437 h 39 min", "Extended identity exposes operating hours in evidenced minute units");
        check(DeviceMetadataCodec.FormatOperatingTime(null) == "—", "Missing operating hours stay unknown instead of zero");
        try { DeviceMetadataCodec.OperatingMinutes(new byte[92], "fixture"); throw new Exception("Truncated metadata accepted"); }
        catch (NotSupportedException) { check(true, "Truncated operating-time payload is rejected"); }
        try { DeviceMetadataCodec.OperatingMinutes(new byte[93], "different-device"); throw new Exception("Mismatched metadata accepted"); }
        catch (NotSupportedException) { check(true, "Operating time cannot come from a mismatched firmware header"); }
        check(service.GetDevice(id).Readings!.CanSetWink && service.GetControlState(id).Wink == false, "Bulk Wink query advertises confirmed off state");
        var beforeWink = service.ExportNativePreset(id);
        check(service.SetWink(id, true).Wink == true, "Wink enables through command and independent bulk readback");
        check(service.SetWink(id, false).Wink == false && service.ExportNativePreset(id).SequenceEqual(beforeWink), "Wink disables without changing native DSP settings");
        peer.IgnoreWinkWrites = true;
        try { service.SetWink(id, true); throw new Exception("Unconfirmed Wink accepted"); }
        catch (InvalidOperationException) { check(service.GetControlState(id).Wink == false, "Wink transport ACK without matching state is rejected"); }
        finally { peer.IgnoreWinkWrites = false; }
        check(service.GetDevice(id).Readings!.CanSetMutes, "Live profile advertises verified mute capabilities");
        service.Apply(service.Plan(new(id, 1, Muted: false)).Id);
        var unmutedNative = service.ExportNativePreset(id);
        service.Apply(service.Plan(new(id, 1, Muted: true)).Id);
        var expectedOutputMute = unmutedNative.ToArray(); expectedOutputMute[36 + 48] = 0;
        check(service.GetDevice(id).Outputs[0].Muted && service.ExportNativePreset(id).SequenceEqual(expectedOutputMute), "Output mute changes only its active-low byte and confirms full readback");
        service.Apply(service.Plan(new(id, 1, Muted: false)).Id);
        check(service.ExportNativePreset(id).SequenceEqual(unmutedNative), "Output unmute restores the original native settings exactly");
        var routeBefore = service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1);
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, Muted: false)).Id);
        check(!service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1).Muted && service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1).GainDb == routeBefore.GainDb, "Route mute uses output-major coordinates and preserves gain");
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, Muted: routeBefore.Muted)).Id);
        peer.IgnoreMuteWrites = true;
        try { service.Apply(service.Plan(new(id, 1, Muted: true)).Id); throw new Exception("Rejected mute accepted"); }
        catch (InvalidOperationException) { check(true, "Mute ACK/delivery without matching native state fails"); }
        finally { peer.IgnoreMuteWrites = false; }
        check(service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Bridged:true)).Id).Bridged, "Bridge confirmed from native pair flag");
        service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Bridged:false)).Id);
        var priorLimiter = service.GetOutputDynamics(id,1);
        var lower = priorLimiter.Peak with { ThresholdVolts = priorLimiter.Peak.ThresholdVolts > 1 ? priorLimiter.Peak.ThresholdVolts - 1 : 1 };
        check(service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Peak:lower)).Id).Peak == lower, "Live limiter edit requires full settings readback");
        service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Peak:priorLimiter.Peak)).Id);
        var mainBefore = service.ExportNativePreset(id);
        check(service.GetDevice(id).Readings is { CanSetInputGain: true, CanSetInputMute: true, CanRename: true }, "Main advertises only its implemented gain/mute/name write capabilities");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, GainDb: .5)).Id);
        var gainExpected = mainBefore.ToArray(); BitConverter.GetBytes(.5f).CopyTo(gainExpected, 36 + 369);
        check(service.GetInputProcessing(id, 1).GainDb == .5 && service.ExportNativePreset(id).SequenceEqual(gainExpected), "Input gain changes exactly its observed float32 native value");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, GainDb: 0)).Id);
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Muted: true)).Id);
        var inputMuteExpected = mainBefore.ToArray(); inputMuteExpected[2192] = 0;
        check(service.GetInputProcessing(id, 1).Muted == true && service.ExportNativePreset(id).SequenceEqual(inputMuteExpected), "Input mute uses side zero and only the native input mute flag");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Muted: false)).Id);
        check(service.ExportNativePreset(id).SequenceEqual(mainBefore), "Input gain and mute restore all native bytes exactly");
        check(service.RenameDevice(id, "New main name").Name == "New main name" && service.ExportNativePreset(id).SequenceEqual(mainBefore), "Rename uses independent identity readback and preserves all DSP settings");
        check(service.RenameDevice(id,new string('x',31)).Name==new string('x',31),"Observed name profile accepts a 31-byte ASCII name with identity readback");
        var writesBeforeInvalidName=peer.Writes;
        try{service.RenameDevice(id,new string('x',32));throw new Exception("32-byte physical name accepted");}
        catch(ArgumentException){check(peer.Writes==writesBeforeInvalidName,"A 32-byte physical name is rejected before any device write");}
        service.RenameDevice(id, "Live fixture");
        peer.IgnoreInputWrites = true;
        try { service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, GainDb: -.5)).Id); throw new Exception("Unconfirmed input gain accepted"); }
        catch (InvalidOperationException) { check(service.GetInputProcessing(id, 1).GainDb == 0, "Input gain delivery without matching settings readback fails"); }
        finally { peer.IgnoreInputWrites = false; }
        peer.IgnoreRenameWrites = true;
        try { service.RenameDevice(id, "Ignored rename"); throw new Exception("Unconfirmed name accepted"); }
        catch (InvalidOperationException) { check(service.GetDevice(id).Name == "Live fixture", "Failed identity name write retains actual device state"); }
        finally { peer.IgnoreRenameWrites = false; }
        peer.AdjustLoadOnMode = true;
        service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,2,LoadMode:1)).Id);
        var lowZ = service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,2,LoadMode:0)).Id);
        check(lowZ.LoadMode == 0 && service.GetNativePresetMetadata(id)!.Channels[1].ConfiguredLoadOhms == 8,
            "Output B confirms Low-Z and adopts ancillary data from the same full readback");
        peer.AdjustLoadOnMode = false;
        var verifiedBefore = service.ExportNativePreset(id);
        var beforeChannel = service.GetDevice(id).Outputs[0];
        service.Apply(service.Plan(new(id,1,GainDb:-.5,DelayMs:1.25,Inverted:true)).Id);
        check(service.GetDevice(id).Outputs[0] is {GainDb:-.5,DelayMs:1.25,Inverted:true}, "Verified output trim, delay and polarity each use independent native readback");
        service.Apply(service.Plan(new(id,1,GainDb:beforeChannel.GainDb,DelayMs:beforeChannel.DelayMs,Inverted:beforeChannel.Inverted)).Id);
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,DelayMs:2)).Id);
        check(service.GetInputProcessing(id,1).DelayMs==2,"Verified input delay uses the captured input-side command");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,DelayMs:0)).Id);
        var routeOriginal = service.GetRouting(id).Cells.Single(r=>r.Input==1 && r.Output==1);
        service.ApplyRouting(service.PlanRouting(new(id,1,1,GainDb:-.5)).Id);
        check(service.GetRouting(id).Cells.Single(r=>r.Input==1 && r.Output==1) is {GainDb:-.5} updatedRoute && updatedRoute.Muted==routeOriginal.Muted,"Routing gain changes preserve independent mute state");
        service.ApplyRouting(service.PlanRouting(new(id,1,1,GainDb:routeOriginal.GainDb)).Id);
        var eqOriginal = service.GetInputProcessing(id,1);
        var editedBands = eqOriginal.Bands.Select(b=>b.Number==5 ? b with {GainDb=.5,FrequencyHz=1000,Q=2,Enabled=false} : b).ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,Bands:editedBands,EqEnabled:false)).Id);
        check(service.GetInputProcessing(id,1).Bands[4] is {GainDb:.5,FrequencyHz:1000,Q:2,Enabled:false} && !service.GetInputProcessing(id,1).EqEnabled,"Captured input Peak gain/frequency/Q/bypass and global bypass are independently applied");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,Bands:eqOriginal.Bands,EqEnabled:eqOriginal.EqEnabled)).Id);
        service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,NoiseGateEnabled:true,NoiseGateDb:-65,LoadMode:1)).Id);
        check(service.GetOutputDynamics(id,1) is {NoiseGateEnabled:true,NoiseGateDb:-65,LoadMode:1},"Captured gate flag, signed threshold and status-zero 70V mode use native readback");
        var originalDynamics = OutputDynamicsWire.DecodeNative(verifiedBefore,1);
        service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,NoiseGateEnabled:originalDynamics.NoiseGateEnabled,NoiseGateDb:originalDynamics.NoiseGateDb,LoadMode:originalDynamics.LoadMode)).Id);
        check(service.SetFirEnabled(id,1,true) is {Enabled:true,CoefficientsKnown:true,Coefficients.Count:512},"FIR bypass works without reading, replacing or inventing coefficient data");
        service.SetFirEnabled(id,1,verifiedBefore[36+368]==0);
        check(service.ExportNativePreset(id).SequenceEqual(verifiedBefore),"All verified DSP edits restore the complete original native settings");
        try {service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,LoadMode:2)).Id);throw new Exception("100V enabled");}
        catch(NotSupportedException){check(true,"Uncaptured 100V mode remains disabled");}
        check(service.FactoryReset(id).Confirmed, "Factory reset accepts the DSP-1004 settings reply and refreshes settings");
        var crossoverBefore = service.GetInputProcessing(id,1);
        var crossoverNative = service.ExportNativePreset(id);
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,HighPass:new(80,4,true),LowPass:new(18000,4,true))).Id);
        var crossoverExpected = crossoverNative.ToArray();
        crossoverExpected[121] = crossoverExpected[247] = 4;
        BitConverter.GetBytes(80f).CopyTo(crossoverExpected,126);
        BitConverter.GetBytes(18000f).CopyTo(crossoverExpected,252);
        check(service.ExportNativePreset(id).SequenceEqual(crossoverExpected), "Input HP/LP use slots zero/nine and alter only type/frequency bytes");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,HighPass:new(80,4,false),LowPass:new(18000,4,false))).Id);
        crossoverExpected[121] = crossoverExpected[247] = 251;
        check(service.ExportNativePreset(id).SequenceEqual(crossoverExpected), "Bypassed BW-24 encodes signed -5 and preserves cutoff frequencies");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,HighPass:crossoverBefore.HighPass,LowPass:crossoverBefore.LowPass)).Id);
        check(service.ExportNativePreset(id).SequenceEqual(crossoverNative), "Restoring HP/LP preserves every native byte including other channels and EQ");
        var crossoverWrites = peer.Writes;
        try {service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,GainDb:-1,HighPass:new(80,1,true))).Id);throw new Exception("Uncaptured crossover type accepted");}
        catch(NotSupportedException){check(peer.Writes==crossoverWrites,"Unsupported crossover rejects the whole change before any gain/filter write");}
        peer.IgnoreFilterWrites = true;
        try {service.ApplyInputProcessing(service.PlanInputProcessing(new(id,1,HighPass:new(80,0,true))).Id);throw new Exception("Unconfirmed crossover accepted");}
        catch(InvalidOperationException){check(service.ExportNativePreset(id).SequenceEqual(crossoverNative),"Crossover delivery without independent native confirmation fails");}
        finally {peer.IgnoreFilterWrites = false;}
        check(peer.DelayedStandbyReads > 0, "Standby readback waits for asynchronous native state without repeating writes");
        var originalPreset = service.ExportNativePreset(id);
        var changedPreset = originalPreset.ToArray();
        Encoding.ASCII.GetBytes("File check").CopyTo(changedPreset,2160);
        service.LoadPresetFile(id,"fixture.sd",changedPreset);
        check(peer.Sync.SequenceEqual(changedPreset), "Physical backend applies a fragmented native preset and confirms it");
        service.LoadPresetFile(id,"restore.sd",originalPreset);
        check(service.StoreDevicePreset(id,24,"Stored").Confirmed && service.RecallDevicePreset(id,24).Confirmed &&
            service.ClearDevicePresets(id).Confirmed, "Physical slots use the reported range and verify store, recall and clear");
        using (var secondPeer = new Peer("demo-2002"))
        {
            var second = service.Connect(target with { RemotePort = secondPeer.Port });
            check(second.Outputs.Count == 2 && service.GetInputProcessing(second.Id, 2).Bands.Count == 8, "Two-channel live snapshot has the same processing functions");
            check(service.ListDevices().Count(d => !d.IsDemo) == 2, "Two live devices share the same local UDP port beside demos");
            var twoNative = service.ExportNativePreset(second.Id);
            var twoInput = service.GetInputProcessing(second.Id,2);
            service.ApplyInputProcessing(service.PlanInputProcessing(new(second.Id,2,HighPass:new(80,4,false))).Id);
            var twoExpected = twoNative.ToArray(); twoExpected[36+515+85]=251;
            BitConverter.GetBytes(80f).CopyTo(twoExpected,36+515+90);
            check(service.ExportNativePreset(second.Id).SequenceEqual(twoExpected), "Two-channel input 2 crossover edits preserve input 1 and all output data");
            service.ApplyInputProcessing(service.PlanInputProcessing(new(second.Id,2,HighPass:twoInput.HighPass)).Id);
            check(service.ExportNativePreset(second.Id).SequenceEqual(twoNative), "Two-channel crossover restoration is byte-exact");
            service.Disconnect(second.Id);
        }
        using (var legacyPeer = new Peer(legacy: true))
        {
            var legacyDevice = service.Connect(target with { RemotePort = legacyPeer.Port });
            check(legacyDevice.Readings!.CurrentPreset == "Legacy fixture", "B118 current preset name is read from the shared native name offset");
            service.Disconnect(legacyDevice.Id);
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
        private readonly List<byte> fragments = [];
        private readonly byte[] slotNames = new byte[768];
        public int Writes, Confirmations, UnexpectedQueries;
        public volatile bool AdjustLoadOnMode;
        public volatile bool IgnoreControlWrites, IgnoreMuteWrites, IgnoreWinkWrites, IgnoreInputWrites, IgnoreRenameWrites, IgnoreFilterWrites;
        private bool wink;
        private string deviceName = "Live fixture";
        private bool standby;
        private long nativeStandbyDue;
        private byte nativeStandbyValue;
        public int DelayedStandbyReads;
        public Peer(string model = "demo-1004", bool legacy = false)
        {
            "Stage"u8.CopyTo(slotNames.AsSpan(23 * 32));
            var service = new AmplifierService(new DemoBackend());
            if(model == "demo-2002") service.AddDemo("DSP-2002");
            var file = service.ExportNativePreset(model);
            Sync = file[..(legacy ? 2232 : NativePresetCodec.Inspect(file).SyncLength)];
            if (legacy)
            {
                Encoding.ASCII.GetBytes("106118").CopyTo(Sync, 9);
                Encoding.ASCII.GetBytes("Legacy fixture").CopyTo(Sync, 2160);
            }
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
                    byte[] inner;
                    if (header.Count > 1)
                    {
                        await socket.SendAsync(Convert.FromHexString("03D9000000C201010100"), received.RemoteEndPoint);
                        if (header.Index == 1) fragments.Clear();
                        fragments.AddRange(received.Buffer[10..]);
                        if (header.Index != header.Count) continue;
                        inner = fragments.ToArray();
                    }
                    else inner = received.Buffer[10..];
                    var message = CvrWire.DecodeInner(inner);
                    if (message is { Function: 27, Status: 1 } && message.Body.Length == Sync.Length)
                    { message.Body.CopyTo(Sync,0); continue; }
                    if (message is { Function: 59, Body.Length: 34, Status: 0 })
                    {
                        int offset = message.Body[1] * 32;
                        if (message.Body[0]==1) message.Body.AsSpan(2,32).CopyTo(slotNames.AsSpan(offset));
                        if (message.Body[0]==2) slotNames.AsSpan(offset,32).CopyTo(Sync.AsSpan(2160));
                        if (message.Body[0]==3) Array.Clear(slotNames);
                        continue;
                    }
                    if(message is {Function:16,Status:0} && message.Body.SequenceEqual(new byte[]{1}))
                    { await Send(new(27,1,32,10,0,Sync),received.RemoteEndPoint); continue; }
                    if(message is {Function:42,Status:1,Side:1,Body.Length:4})
                    { message.Body.CopyTo(Sync,36+message.Channel*515+76); continue; }
                    if(message is {Function:50,Side:1,Body.Length:1})
                    { Sync[34+message.Channel]=message.Body[0]; continue; }
                    if (message is { Function: 83, Channel: 0, Segment: 0, Side: 0 })
                    {
                        if (message is { Status: 0, Body.Length: 1 })
                        { Interlocked.Increment(ref Writes); if (!IgnoreWinkWrites) wink = message.Body[0] == 1; }
                        else if (message is { Status: 2, Body.Length: 0 })
                        { await Send(new(83, 1, 32, 10, 0, [(byte)(wink ? 1 : 0)]), received.RemoteEndPoint); }
                        else Interlocked.Increment(ref UnexpectedQueries);
                        continue;
                    }
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
                    if (message is { Status: 0, Function: 52, Side: 1, Body.Length: 141 })
                    {
                        message.Body.CopyTo(Sync, 36 + message.Channel * 515 + 226);
                        continue;
                    }
                    if (message is { Status: 1, Function: 9 or 14, Body.Length: 4 })
                    {
                        Interlocked.Increment(ref Writes);
                        var offset = message.Function == 9 ? (message.Side == 0 ? 369 : 44) : (message.Side == 0 ? 50 : 54);
                        if (!(message.Side == 0 && IgnoreInputWrites)) message.Body.CopyTo(Sync, 36 + message.Channel * 515 + offset);
                        continue;
                    }
                    if ((message is { Status: 1, Function: 18 or 44 or 69 or 87, Side: 1, Body.Length: 1 }) ||
                        (message is { Status: 0, Function: 49, Side: 1, Body.Length: 1 }))
                    {
                        Interlocked.Increment(ref Writes);
                        if (AdjustLoadOnMode && message.Function == 49) BitConverter.GetBytes(message.Body[0] == 0 ? 8f : 16f).CopyTo(Sync,36+message.Channel*515+374);
                        var offset = message.Function switch { 18=>58,44=>368,49=>367,69=>373,_=>-1 };
                        Sync[offset < 0 ? 2232 + message.Channel : 36 + message.Channel * 515 + offset] = message.Body[0];
                        continue;
                    }
                    if (message is { Status: 1, Function: 33, Body.Length: 8 })
                    {
                        var record = 36 + message.Channel * 515 + (message.Side == 0 ? 85 : 226) + message.Segment * 14;
                        message.Body.AsSpan(0, 4).CopyTo(Sync.AsSpan(record + 5));
                        message.Body.AsSpan(4, 4).CopyTo(Sync.AsSpan(record + 1));
                        continue;
                    }
                    if (message is { Status: 1, Function: 30 or 31 or 32 or 34 or 36, Side: 0 })
                    {
                        Interlocked.Increment(ref Writes);
                        if (IgnoreFilterWrites) continue;
                        var offset = message.Function == 36 ? 225 : 85 + message.Segment * 14 + (message.Function switch {31=>1,32=>5,34=>9,_=>0});
                        message.Body.CopyTo(Sync,36 + message.Channel * 515 + offset);
                        continue;
                    }
                    if (message is { Status: 1, Function: 60, Side: 0, Body.Length: 32 })
                    { Interlocked.Increment(ref Writes); if (!IgnoreRenameWrites) deviceName = Encoding.ASCII.GetString(message.Body).TrimEnd('\0'); continue; }
                    if (message.Status == 1 && message.Function is 10 or 12)
                    {
                        Interlocked.Increment(ref Writes);
                        if (!IgnoreMuteWrites)
                        {
                            if (message.Function == 10) Sync[message.Side == 0 ? 2192 + message.Channel : 36 + message.Channel * 515 + 48] = message.Body[0];
                            else message.Body.CopyTo(Sync, 36 + message.Channel * 515 + 24 + message.Segment * 5);
                        }
                        continue;
                    }
                    if (message.Status == 1) { Interlocked.Increment(ref Writes); continue; }
                    if (message is { Function: 27, Status: 3, Body.Length: 0 })
                    { Interlocked.Increment(ref Confirmations); continue; }
                    if (message is { Function: 43, Status: 2, Side: 1, Body.Length: 0 })
                    {
                        var taps = new byte[2080];
                        BitConverter.GetBytes(0.5f).CopyTo(taps, 32);
                        await Send(new(43, 1, message.Channel, 10, 1, taps), received.RemoteEndPoint);
                        continue;
                    }
                    if (message is { Function: 81, Status: 2, Body.Length: 0 })
                    {
                        var allocation = new byte[40];
                        BitConverter.GetBytes(8f).CopyTo(allocation, 8);
                        await Send(new(81, 1, 32, 10, 0, allocation), received.RemoteEndPoint);
                        continue;
                    }
                    if (message is { Function: 91, Status: 2, Channel: 2, Segment: 0, Side: 1, Body.Length: 0 })
                    {
                        var limiter = new byte[30];
                        Sync.AsSpan(36 + 2 * 515 + 59, 26).CopyTo(limiter);
                        BitConverter.GetBytes(6f).CopyTo(limiter, 26);
                        await Send(new(91, 1, 2, 10, 1, limiter), received.RemoteEndPoint);
                        continue;
                    }
                    if (message is { Function: 59, Status: 2, Body.Length: 34 } && message.Body.All(b => b == 0))
                    {
                        var table = slotNames;

                        await Send(new(59, 1, 32, 10, 0, table), received.RemoteEndPoint);
                        continue;
                    }
                    if (message.Status != 2 || message.Function is not (0 or 6 or 27 or 71))
                    { Interlocked.Increment(ref UnexpectedQueries); continue; }
                    await socket.SendAsync(Convert.FromHexString("03D9000000C201010100"), received.RemoteEndPoint, stop.Token);
                    if (message.Function == 71)
                    {
                        var extended = new byte[93]; Sync[..32].CopyTo(extended, 42);
                        BitConverter.GetBytes(26259u).CopyTo(extended, 74);
                        await Send(new(71, 1, 32, 10, 0, extended), received.RemoteEndPoint);
                    }
                    else if (message.Function is 0 or 27)
                    {
                        var identity = new byte[79]; Sync[..32].CopyTo(identity, 0);
                        Encoding.ASCII.GetBytes(deviceName).CopyTo(identity, 32);
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
                            BitConverter.GetBytes(i == 0 ? 0.1f : 0f).CopyTo(data, 56 + i * 4);
                            data[88 + i] = (byte)(i == 1 ? 0 : 1);
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
