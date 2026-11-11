// Checks / Synthetic CVR peer
// Purpose: exercise actual UDP framing, readback and shared physical-backend paths.
// Input: independently built synthetic packets on loopback only.
// State: the fake amplifier stores writes and can reject them or drop replies.
// Scope: these checks do not constitute testing on a physical amplifier.
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CvrTool.Application;
using CvrTool.Core;
using CvrTool.Protocol;

internal static class CvrPeerChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(SessionDefaults.Resolve(new string('x', 512)).Length <= 24, "Long session labels have bounded portable socket names");
        check(SessionDefaults.Resolve("one") != SessionDefaults.Resolve("two") && SessionDefaults.Resolve(null) == SessionDefaults.PipeName, "Session normalization preserves isolation and default ownership");
        // Capture-derived display-lock fixture, excluding all addresses and device identifiers.
        var locked = CvrWire.Encode(new(17, 1, 0, 0, 0, [1]));
        check(Convert.ToHexString(locked) == "03D90000010E000100005511010000000000000001000E76", "Display lock framing matches the observed command bytes");
        var corrupt = locked[10..];
        corrupt[^1] ^= 1;
        try
        {
            CvrWire.DecodeInner(corrupt);
            throw new Exception("Checksum accepted");
        }
        catch (InvalidDataException) { check(true, "Corrupt checksum rejected"); }
        using var peer = new Peer();
        using var backend = new ConnectionBackend();
        var service = new AmplifierService(backend);
        var device = service.Connect(new("Synthetic peer", "127.0.0.1", peer.Port, "127.0.0.1", 0, 250));
        string id = device.Id;
        var pipe = "cvr-wire-check-" + Guid.NewGuid().ToString("N");
        using var host = new SessionHost(backend, pipe);
        var remoteService = new AmplifierService(new SessionBackend(pipe));
        check(remoteService.GetDevice(id).Id == id, "Connected peer is accessible through shared-session IPC");
        check(!device.IsDemo && device.Outputs.Count == 2 && device.Outputs[0].GainDb == -12, "Physical backend reads identity and settings instead of simulation defaults");
        var plan = service.Plan(new(id, 1, GainDb: -6, Muted: false, DelayMs: 2.5, Inverted: true));
        check(service.GetDevice(id).Outputs[0].GainDb == -12, "LAN plan performs no writes");
        check(remoteService.Apply(remoteService.Plan(new(id, 2, GainDb: -7)).Id).GainDb == -7, "Shared-session client writes to the same UDP owner");
        plan = service.Plan(new(id, 1, GainDb: -6, Muted: false, DelayMs: 2.5, Inverted: true));
        check(service.Apply(plan.Id) is { GainDb: -6, Muted: false, DelayMs: 2.5, Inverted: true }, "Output writes are confirmed by independent reads");
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, GainDb: -3, Muted: false)).Id);
        check(service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1) is { GainDb: -3, Muted: false } && peer.Values[(12, 1, 0, 0)][4] == 0, "Matrix wire orientation is input/channel and output/segment");
        var input = service.GetInputProcessing(id, 1);
        var bands = input.Bands.Select(b => b with { GainDb = b.Number == 1 ? 3 : b.GainDb }).ToArray();
        var filters = peer.Values[(52, 0, 0, 0)].ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, DelayMs: 10, EqEnabled: false, Bands: bands)).Id);
        var updated = service.GetInputProcessing(id, 1);
        check(updated.DelayMs == 10 && !updated.EqEnabled && updated.Bands[0].GainDb == 3, "Input delay and EQ use readback");
        var raw = peer.Values[(52, 0, 0, 0)];
        check(filters[..14].SequenceEqual(raw[..14]) && filters[126..140].SequenceEqual(raw[126..140]) && raw[14] == filters[14] && raw[27] == filters[27], "EQ writes preserve crossover, filter type and reset fields");
        var bandsBeforeCrossover = raw[14..126].ToArray();
        foreach (var option in CrossoverFilters.Options)
        {
            var hp = new CrossoverFilter(80, option.Code, true);
            var lp = new CrossoverFilter(16000, option.Code, false);
            var crossover = service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, HighPass: hp, LowPass: lp)).Id);
            var observed = peer.Values[(52, 0, 0, 0)];
            check(crossover.HighPass == hp && crossover.LowPass == lp && observed[0] == option.Code && observed[126] == 255 - option.Code && observed[14..126].SequenceEqual(bandsBeforeCrossover), "Crossover type/frequency/bypass readback preserves EQ: " + option.ShortName);
        }
        foreach(var mode in new[]{0,1,2})
        {
            remoteService.ApplyOutputDynamics(remoteService.PlanOutputDynamics(new(id,1,LoadMode:mode)).Id);
            check(service.GetOutputDynamics(id,1).LoadMode==mode && service.GetOutputDynamics(id,2).LoadMode==0 && peer.Values[(49,0,0,1)][0]==mode && peer.Values[(50,0,0,1)][0]==1,"Per-output load mode uses function 49 and preserves paired bridge");
        }
        var dyn=remoteService.GetOutputDynamics(id,1);
        var rmsState=dyn.Rms with {ThresholdVolts=50,AttackMs=40,ReleaseMultiplier=8,Automatic=true,Enabled=false};var peakState=dyn.Peak with {ThresholdVolts=90,HoldMs=150,ReleaseMs=500,Enabled=false};
        remoteService.ApplyOutputDynamics(remoteService.PlanOutputDynamics(new(id,1,Bridged:true,NoiseGateEnabled:true,NoiseGateDb:-65,Rms:rmsState,Peak:peakState)).Id);
        check(service.GetOutputDynamics(id,1) is {Bridged:true,NoiseGateEnabled:true,NoiseGateDb:-65} && service.GetOutputDynamics(id,2).Bridged,"Mode pair and signed gate threshold round-trip through shared IPC");
        check(service.GetOutputDynamics(id,1).Rms==rmsState && service.GetOutputDynamics(id,1).Peak==peakState,"All RMS/peak settings use independent aggregate readback");
        check(peer.Values[(50,0,0,1)][0]==0 && peer.Values[(69,0,0,1)][0]==0 && peer.Values[(87,0,0,1)][0]==191 && BitConverter.ToSingle(peer.Values[(55,0,0,1)],9)==100,"Dynamics encoding preserves immutable maximums and active-low flags");
        peer.IgnoreWrites=true;
        try{service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,NoiseGateDb:-50)).Id);throw new Exception("Dynamics write rejection ignored");}
        catch(InvalidOperationException){check(true,"A gate write without matching readback fails explicitly");}finally{peer.IgnoreWrites=false;}
        peer.IgnoreDynamicsAggregate=true;
        try{service.ApplyOutputDynamics(service.PlanOutputDynamics(new(id,1,Peak:peakState with {ThresholdVolts=80})).Id);throw new Exception("Aggregate mismatch ignored");}
        catch(InvalidOperationException){check(true,"Individual ACK/readback without matching aggregate state is not success");}finally{peer.IgnoreDynamicsAggregate=false;}
        var outputBands=remoteService.GetOutputProcessing(id,1).Bands.Select(b=>b with {GainDb=b.Number==2 ? -5 : 0}).ToArray();
        remoteService.ApplyOutputProcessing(remoteService.PlanOutputProcessing(new(id,1,DelayMs:7,Bands:outputBands,LowPass:new(18000,6,true))).Id);
        check(service.GetOutputProcessing(id,1).Bands[1].GainDb==-5 && service.GetInputProcessing(id,1).Bands[0].GainDb==3 && service.GetDevice(id).Outputs[0].DelayMs==7,"Output EQ side selection and delay readback work through IPC");
        var taps=Enumerable.Range(0,512).Select(n=>n==123 ? .5 : 0).ToArray();
        var storedFir=remoteService.SetFir(id,new(1,"Synthetic FIR",taps,true));
        check(storedFir.Enabled && storedFir.Coefficients.Count==512 && storedFir.Coefficients[123]==.5 && storedFir.Name=="Synthetic FIR" && peer.Values[(43,0,0,1)].Length==2080,"512-tap FIR upload fragments and independent readback round-trip");
        check(service.GetFir(id,2) is {Enabled:false,Name:""},"FIR changes isolate other outputs");
        check(remoteService.GetDevicePresets(id).Slots.Count==16,"Fragmented 512-byte preset names are reassembled through IPC");
        check(remoteService.StoreDevicePreset(id,16,"Last slot").Confirmed && service.GetDevicePresets(id).Slots[15].Name=="Last slot","Preset command semantic ACK and slot-16 name readback are required");
        check(remoteService.RecallDevicePreset(id,16).Confirmed,"Preset recall requires semantic acknowledgement");
        check(remoteService.ClearDevicePresets(id).Confirmed && service.GetDevicePresets(id).Slots.All(s=>!s.Occupied),"Clear verifies all sixteen names empty");
        check(remoteService.FactoryReset(id).Confirmed,"Factory reset requires semantic acknowledgement and settings refresh");
        check(service.RenameDevice(id, "Bench amp").Name == "Bench amp", "Device name write is read back through basic identity");
        check(service.SetControlState(id, displayLocked: true, standby: true) == new DeviceControlState(true, true), "Lock and standby round-trip");
        var readings = service.GetTelemetry(id);
        check(readings[0] is { Voltage: 40, Current: 5, LoadOhms: 8, TemperatureC: null, LevelDbFs: null } && readings[0].Status!.Clip == true && readings[1].Status!.Clip == false, "Two-channel telemetry keeps channel temperature and dBFS unknown");
        var stale = service.Plan(new(id, 1, GainDb: -4));
        peer.Values[(9, 0, 0, 1)] = BitConverter.GetBytes(-8f);
        try
        {
            service.Apply(stale.Id);
            throw new Exception("External change accepted");
        }
        catch (InvalidOperationException) { check(true, "Observed external edit rejects stale plan"); }
        peer.IgnoreWrites = true;
        try
        {
            service.Apply(service.Plan(new(id, 1, GainDb: -2)).Id);
            throw new Exception("Rejected write accepted");
        }
        catch (InvalidOperationException) { check(true, "Transport delivery without matching readback is not success"); }
        peer.DropReplies = true;
        try
        {
            service.GetDevice(id);
            throw new Exception("Missing reply accepted");
        }
        catch (TimeoutException) { check(true, "Missing replies time out without invented state"); }
        peer.DropReplies = false;
        check(service.Disconnect(id).Disconnected && service.ListDevices().All(d => d.IsDemo), "Disconnect releases the target");
    }

    private sealed class Peer : IDisposable
    {
        private readonly UdpClient socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource stop = new();
        private readonly Task listener;
        public int Port => ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<(byte, byte, byte, byte), byte[]> Values = new();
        public volatile bool IgnoreWrites, DropReplies, IgnoreDynamicsAggregate;
        public Peer()
        {
            var basic = new byte[75];
            Encoding.ASCII.GetBytes("Synthetic DSP-2002").CopyTo(basic, 0);
            Encoding.ASCII.GetBytes("Test amp").CopyTo(basic, 32);
            basic[71] = basic[73] = 2;
            Values[(0, 0, 0, 0)] = basic;
            Values[(15, 0, 0, 0)] = [0];
            Values[(50,0,0,1)]=[1];
            Values[(17, 0, 0, 0)] = [0];
            for (byte ch = 0; ch < 2; ch++)
            {
                Values[(49,ch,0,1)]=[0];Values[(69,ch,0,1)]=[1];Values[(87,ch,0,1)]=[unchecked((byte)(sbyte)-100)];
                var rms=new byte[13];BinaryPrimitives.WriteInt16LittleEndian(rms,25);rms[2]=16;BitConverter.GetBytes(80f).CopyTo(rms,3);rms[8]=1;BitConverter.GetBytes(100f).CopyTo(rms,9);Values[(55,ch,0,1)]=rms;
                var peak=new byte[13];BinaryPrimitives.WriteInt16LittleEndian(peak,50);BinaryPrimitives.WriteInt16LittleEndian(peak.AsSpan(2),300);BitConverter.GetBytes(120f).CopyTo(peak,4);BitConverter.GetBytes(140f).CopyTo(peak,9);Values[(54,ch,0,1)]=peak;
                foreach(var (fn,offset,length) in new[]{(37,0,2),(38,2,1),(39,3,4),(46,7,1),(48,8,1)})Values[((byte)fn,ch,0,1)]=rms[offset..(offset+length)];
                foreach(var (fn,offset,length) in new[]{(40,0,2),(41,2,2),(42,4,4),(47,8,1)})Values[((byte)fn,ch,0,1)]=peak[offset..(offset+length)];
                Values[(9, ch, 0, 1)] = BitConverter.GetBytes(-12f);
                Values[(10, ch, 0, 1)] = [1];
                Values[(14, ch, 0, 1)] = BitConverter.GetBytes(0f);
                Values[(18, ch, 0, 1)] = [0];
                Values[(14, ch, 0, 0)] = BitConverter.GetBytes(0f);
                var eq = new byte[141];
                for (int band = 0; band < 10; band++)
                {
                    eq[band * 14] = 2;
                    BitConverter.GetBytes(100f).CopyTo(eq, band * 14 + 5);
                    BitConverter.GetBytes(1f).CopyTo(eq, band * 14 + 9);
                    eq[band * 14 + 13] = 3;
                }
                Values[(52, ch, 0, 0)] = eq;
                Values[(52, ch, 0, 1)] = eq.ToArray();
                Values[(44, ch, 0, 1)] = [1];
                var fir=new byte[2080];BitConverter.GetBytes(1f).CopyTo(fir,32);Values[(43,ch,0,1)]=fir;
                for (byte output = 0; output < 2; output++)
                    Values[(12, ch, output, 0)] = [0, 0, 0, 0, 1];
            }
            Values[(59,0,0,0)]=new byte[512];
            var telemetry = new byte[32];
            BitConverter.GetBytes(40f).CopyTo(telemetry, 4);
            BitConverter.GetBytes(5f).CopyTo(telemetry, 12);
            telemetry[20] = 5;
            Values[(6, 0, 0, 0)] = telemetry;
            listener = Task.Run(Listen);
        }
        private async Task Listen()
        {
            byte[][]? pending=null;
            try
            {
                while(!stop.IsCancellationRequested)
                {
                    var received=await socket.ReceiveAsync(stop.Token);var packet=received.Buffer;
                    if(packet.Length<10 || packet[8]==1)continue;
                    var count=packet[4];var index=packet[7];
                    if(count>1)
                    {
                        var ack=packet[..10];ack[8]=1;await socket.SendAsync(ack,received.RemoteEndPoint,stop.Token);
                        pending ??=new byte[count][];pending[index-1]=packet[10..];
                        if(pending.Any(f=>f is null))continue;
                        packet=new byte[10].Concat(pending.SelectMany(f=>f)).ToArray();pending=null;
                    }
                    byte function=packet[11],state=packet[12],channel=packet[13],segment=packet[14],side=packet[19];
                    var key=(function,channel,segment,side);var request=packet[20..^3];byte[]? body=null;byte responseState=0;
                    if(state==1)
                    {
                        if(!IgnoreWrites)
                        {
                            if(function==60){var basic=Values[(0,0,0,0)].ToArray();request.CopyTo(basic,32);Values[(0,0,0,0)]=basic;}
                            else
                            {
                                Values[key]=request;
                                var rmsMap=new Dictionary<byte,int>{{37,0},{38,2},{39,3},{46,7},{48,8}};var peakMap=new Dictionary<byte,int>{{40,0},{41,2},{42,4},{47,8}};
                                if(!IgnoreDynamicsAggregate && rmsMap.TryGetValue(function,out var ro)){var raw=Values[(55,channel,0,1)].ToArray();request.CopyTo(raw,ro);Values[(55,channel,0,1)]=raw;}
                                if(!IgnoreDynamicsAggregate && peakMap.TryGetValue(function,out var po)){var raw=Values[(54,channel,0,1)].ToArray();request.CopyTo(raw,po);Values[(54,channel,0,1)]=raw;}
                            }
                        }
                        continue;
                    }
                    if(state==0)
                    {
                        if(function==59)
                        {
                            var names=Values[(59,0,0,0)].ToArray();
                            if(request[0]==1)request[2..34].CopyTo(names,request[1]*32);
                            if(request[0]==3)Array.Clear(names);
                            Values[(59,0,0,0)]=names;
                        }
                        body=[];responseState=3;
                    }
                    else Values.TryGetValue(key,out body);
                    if(DropReplies || body is null)continue;
                    // Independent inner/fragment construction; no production codec used.
                    var inner=new byte[13+body.Length];inner[0]=85;inner[1]=function;inner[2]=responseState;inner[3]=channel;inner[4]=segment;inner[9]=side;body.CopyTo(inner,10);
                    BinaryPrimitives.WriteUInt16BigEndian(inner.AsSpan(inner.Length-3),(ushort)inner.Length);inner[^1]=unchecked((byte)inner[..^1].Sum(b=>b));
                    var fragments=(inner.Length+449)/450;var last=inner.Length-(fragments-1)*450;
                    for(var n=0;n<fragments;n++)
                    {
                        var chunk=inner.Skip(n*450).Take(450).ToArray();var reply=new byte[10+chunk.Length];reply[0]=3;reply[1]=217;reply[2]=202;reply[4]=(byte)fragments;reply[7]=(byte)(n+1);reply[9]=0x21;
                        BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(5),(ushort)last);chunk.CopyTo(reply,10);await socket.SendAsync(reply,received.RemoteEndPoint,stop.Token);
                    }
                }
            }
            catch(OperationCanceledException) { }
        }
        public void Dispose()
        {
            stop.Cancel();
            listener.GetAwaiter().GetResult();
            socket.Dispose();
            stop.Dispose();
        }
    }
}
