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
        var unlocked=CvrWire.Encode(new(17,1,0,0,0,[0]));
        check(Convert.ToHexString(unlocked)=="03D90000010E000100005511010000000000000000000E75","Display unlock framing matches the controlled physical capture");
        // Device/model identifiers and padding are removed from this framing-only fixture.
        var fixedAck=Convert.FromHexString("03D9000000C201010100");
        check(CvrWire.Header(fixedAck)==(0,450,1,true),"Observed fixed D1004 ACK framing is accepted without assuming an address echo");
        foreach(var mutation in new[]{(Offset:8,Value:(byte)0),(Offset:5,Value:(byte)193),(Offset:7,Value:(byte)2),(Offset:0,Value:(byte)0)})
        {
            var invalid=fixedAck.ToArray();invalid[mutation.Offset]=mutation.Value;
            try{CvrWire.Header(invalid);throw new Exception("Invalid fixed ACK accepted");}
            catch(InvalidDataException){check(true,"Fixed ACK exception does not relax malformed/data packet validation");}
        }
        try{CvrWire.Header(fixedAck.Concat(new byte[]{0}).ToArray());throw new Exception("Trailing ACK byte accepted");}
        catch(InvalidDataException){check(true,"Fixed ACK must contain exactly its ten-byte header");}
        check(Convert.ToHexString(CvrWire.Encode(new(9,1,0,0,0,BitConverter.GetBytes(4f))))=="03D900000111000100005509010000000000000000008040001130","Input A 4 dB command matches the controlled capture exactly");
        check(Convert.ToHexString(CvrWire.Encode(new(9,1,0,0,0,BitConverter.GetBytes(4.5f))))=="03D900000111000100005509010000000000000000009040001140","Input A 4.5 dB command matches the controlled capture exactly");
        check(Convert.ToHexString(CvrWire.Encode(new(9, 1, 0, 0, 1, BitConverter.GetBytes(-.5f)))) == "03D9000001110001000055090100000000000001000000BF001130", "Output A -0.5 dB trim command matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(9, 1, 0, 0, 1, BitConverter.GetBytes(0f)))) == "03D900000111000100005509010000000000000100000000001171", "Output A zero trim command matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(10, 1, 0, 0, 0, [0]))) == "03D90000010E00010000550A010000000000000000000E6E", "Input 1 mute matches controlled side-zero active-low capture");
        check(Convert.ToHexString(CvrWire.Encode(new(10, 1, 0, 0, 0, [1]))) == "03D90000010E00010000550A010000000000000001000E6F", "Input 1 unmute matches controlled side-zero capture");
        check(Convert.ToHexString(CvrWire.Encode(new(10, 1, 0, 0, 1, [0]))) == "03D90000010E00010000550A010000000000000100000E6F", "Output A mute matches the user-confirmed active-low capture");
        check(Convert.ToHexString(CvrWire.Encode(new(10, 1, 0, 0, 1, [1]))) == "03D90000010E00010000550A010000000000000101000E70", "Output A unmute matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(14, 1, 0, 0, 1, BitConverter.GetBytes(8.89f)))) == "03D90000011100010000550E0100000000000001713D0E41001173", "Output A 8.89 ms delay matches the user-confirmed output control capture");
        check(Convert.ToHexString(CvrWire.Encode(new(14, 1, 0, 0, 1, BitConverter.GetBytes(0f)))) == "03D90000011100010000550E010000000000000100000000001176", "Output A zero delay matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(14, 1, 0, 0, 0, BitConverter.GetBytes(8.89f)))) == "03D90000011100010000550E0100000000000000713D0E41001172", "Input A 8.89 ms delay uses side zero and matches the capture");
        check(Convert.ToHexString(CvrWire.Encode(new(14, 1, 0, 0, 0, BitConverter.GetBytes(0f)))) == "03D90000011100010000550E010000000000000000000000001175", "Input A zero delay matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(12, 1, 0, 0, 0, [0, 0, 0, 0, 0]))) == "03D90000011200010000550C01000000000000000000000000001274", "Route 1-A mute matches the controlled active-low capture");
        check(Convert.ToHexString(CvrWire.Encode(new(12, 1, 0, 0, 0, [0, 0, 0, 0, 1]))) == "03D90000011200010000550C01000000000000000000000001001275", "Route 1-A unmute matches the controlled capture without changing gain");
        check(Convert.ToHexString(CvrWire.Encode(new(12, 1, 0, 1, 0, [0, 0, 0, 191, 1]))) == "03D90000011200010000550C0100010000000000000000BF01001235", "Captured Input 2 to Output A gain uses output channel zero and input segment one");
        check(Convert.ToHexString(CvrWire.Encode(new(12, 1, 0, 1, 0, [205, 204, 76, 62, 0]))) == "03D90000011200010000550C0100010000000000CDCC4C3E00001298", "Captured off-diagonal re-mute preserves the final +0.2 dB mouse value");
        check(Convert.ToHexString(CvrWire.Encode(new(36, 1, 0, 0, 0, [1]))) == "03D90000010E000100005524010000000000000001000E89", "Input EQ bypass matches the controlled off command");
        check(Convert.ToHexString(CvrWire.Encode(new(36, 1, 0, 0, 0, [0]))) == "03D90000010E000100005524010000000000000000000E88", "Input EQ enable matches the controlled on command");
        check(Convert.ToHexString(CvrWire.Encode(new(32, 1, 0, 5, 0, BitConverter.GetBytes(1250f)))) == "03D900000111000100005520010005000000000000409C440011AC", "Input 1 EQ5 1250 Hz frequency matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(32, 1, 0, 5, 0, BitConverter.GetBytes(1200f)))) == "03D900000111000100005520010005000000000000009644001166", "Input 1 EQ5 1200 Hz frequency matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(31, 1, 0, 5, 0, BitConverter.GetBytes(4.5f)))) == "03D90000011100010000551F01000500000000000000904000115B", "Input 1 EQ5 gain +4.5 dB matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(31, 1, 0, 5, 0, BitConverter.GetBytes(5f)))) == "03D90000011100010000551F01000500000000000000A04000116B", "Input 1 EQ5 gain +5 dB matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(34, 1, 0, 5, 0, BitConverter.GetBytes(3.5f)))) == "03D90000011100010000552201000500000000000000604000112E", "Input 1 EQ5 Q 3.5 matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(34, 1, 0, 5, 0, BitConverter.GetBytes(4f)))) == "03D90000011100010000552201000500000000000000804000114E", "Input 1 EQ5 Q 4 matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(30, 1, 0, 5, 0, [255]))) == "03D90000010E00010000551E0100050000000000FF000E86", "Input EQ5 Peak bypass matches the controlled FF command");
        check(Convert.ToHexString(CvrWire.Encode(new(30, 1, 0, 5, 0, [0]))) == "03D90000010E00010000551E010005000000000000000E87", "Input EQ5 Peak enable matches the controlled zero command");
        check(Convert.ToHexString(CvrWire.Encode(new(18, 1, 0, 0, 1, [1]))) == "03D90000010E000100005512010000000000000101000E78", "Output A inverted polarity matches the controlled 180-degree command");
        check(Convert.ToHexString(CvrWire.Encode(new(18, 1, 0, 0, 1, [0]))) == "03D90000010E000100005512010000000000000100000E77", "Output A normal polarity matches the controlled zero-degree command");
        check(Convert.ToHexString(CvrWire.Encode(new(49, 0, 0, 0, 1, [1]))) == "03D90000010E000100005531000000000000000101000E96", "Output A 70V mode uses observed status zero");
        check(Convert.ToHexString(CvrWire.Encode(new(49, 0, 0, 0, 1, [0]))) == "03D90000010E000100005531000000000000000100000E95", "Output A Low-Z mode matches the controlled return command");
        check(Convert.ToHexString(CvrWire.Encode(new(15, 0, 0, 0, 0, [1]))) == "03D90000010E00010000550F000000000000000001000E73", "Standby on matches the observed status-zero command");
        check(Convert.ToHexString(CvrWire.Encode(new(15, 0, 0, 0, 0, [0]))) == "03D90000010E00010000550F000000000000000000000E72", "Standby off matches the observed status-zero command");
        check(Convert.ToHexString(CvrWire.Encode(new(69, 1, 0, 0, 1, [0]))) == "03D90000010E000100005545010000000000000100000EAA", "Output A noise-gate enable matches the controlled active-low capture");
        check(Convert.ToHexString(CvrWire.Encode(new(69, 1, 0, 0, 1, [1]))) == "03D90000010E000100005545010000000000000101000EAB", "Output A noise-gate disable matches the controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(87, 1, 0, 0, 1, [0xBF]))) == "03D90000010E0001000055570100000000000001BF000E7B", "Output A gate threshold -65 dB matches controlled signed-byte capture");
        check(Convert.ToHexString(CvrWire.Encode(new(87, 1, 0, 0, 1, [0xBA]))) == "03D90000010E0001000055570100000000000001BA000E76", "Output A gate threshold -70 dB restoration matches controlled capture");
        check(Convert.ToHexString(CvrWire.Encode(new(44, 1, 0, 0, 1, [0]))) == "03D90000010E00010000552C010000000000000100000E91", "Output A FIR enable matches the controlled active-low bypass capture");
        check(Convert.ToHexString(CvrWire.Encode(new(44, 1, 0, 0, 1, [1]))) == "03D90000010E00010000552C010000000000000101000E92", "Output A FIR disable matches the controlled bypass capture");
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
        check(!device.Outputs[0].Muted && !device.Outputs[1].Muted, "Output wire value 1 decodes as unmuted");
        service.Apply(service.Plan(new(id, 1, Muted: true)).Id);
        check(peer.Values[(10, 0, 0, 1)][0] == 0 && service.GetDevice(id).Outputs[0].Muted && !service.GetDevice(id).Outputs[1].Muted, "Mute sends zero and isolates the selected output");
        service.Apply(service.Plan(new(id, 1, Muted: false)).Id);
        check(peer.Values[(10, 0, 0, 1)][0] == 1 && !service.GetDevice(id).Outputs[0].Muted, "Unmute sends one with independent readback");
        var plan = service.Plan(new(id, 1, GainDb: -6, Muted: false, DelayMs: 2.5, Inverted: true));
        check(service.GetDevice(id).Outputs[0].GainDb == -12, "LAN plan performs no writes");
        check(remoteService.Apply(remoteService.Plan(new(id, 2, GainDb: -7)).Id).GainDb == -7, "Shared-session client writes to the same UDP owner");
        plan = service.Plan(new(id, 1, GainDb: -6, Muted: false, DelayMs: 2.5, Inverted: true));
        check(service.Apply(plan.Id) is { GainDb: -6, Muted: false, DelayMs: 2.5, Inverted: true }, "Output writes are confirmed by independent reads");
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, GainDb: -3, Muted: false)).Id);
        check(service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1) is { GainDb: -3, Muted: false } && peer.Values[(12, 0, 1, 0)][4] == 1, "Matrix wire orientation is output/channel and input/segment");
        check(BitConverter.ToSingle(peer.Values[(12, 1, 0, 0)]) == 0, "Off-diagonal edit does not write the transposed route");
        var untouchedRoute = service.GetRouting(id).Cells.Single(c => c.Input == 1 && c.Output == 2);
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, Muted: true)).Id);
        check(peer.Values[(12, 0, 1, 0)][4] == 0 && service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1) is { Muted: true, GainDb: -3 }, "Route mute sends zero and preserves gain");
        check(service.GetRouting(id).Cells.Single(c => c.Input == 1 && c.Output == 2) == untouchedRoute, "Route mute preserves unrelated cells");
        service.ApplyRouting(service.PlanRouting(new(id, 2, 1, Muted: false)).Id);
        check(peer.Values[(12, 0, 1, 0)][4] == 1 && !service.GetRouting(id).Cells.Single(c => c.Input == 2 && c.Output == 1).Muted, "Route unmute sends one and verifies readback");
        var outputBeforeInputGain=service.GetDevice(id).Outputs[0].GainDb;
        remoteService.ApplyInputProcessing(remoteService.PlanInputProcessing(new(id,1,GainDb:4)).Id);
        check(service.GetInputProcessing(id,1).GainDb==4 && BitConverter.ToSingle(peer.Values[(9,0,0,0)])==4 && service.GetDevice(id).Outputs[0].GainDb==outputBeforeInputGain,"Input gain writes/readback use side 0 and preserve output side 1 through shared IPC");
        var input = service.GetInputProcessing(id, 1);
        var bands = input.Bands.Select(b => b with { GainDb = b.Number == 1 ? 3 : b.GainDb }).ToArray();
        var filters = peer.Values[(52, 0, 0, 0)].ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, DelayMs: 10, EqEnabled: false, Bands: bands)).Id);
        var updated = service.GetInputProcessing(id, 1);
        check(updated.DelayMs == 10 && !updated.EqEnabled && updated.Bands[0].GainDb == 3, "Input delay and EQ use readback");
        var raw = peer.Values[(52, 0, 0, 0)];
        check(filters[..14].SequenceEqual(raw[..14]) && filters[126..140].SequenceEqual(raw[126..140]) && raw[14] == filters[14] && raw[27] == filters[27], "EQ writes preserve crossover, filter type and reset fields");
        check(peer.Values[(36, 0, 0, 0)][0] == 1 && peer.Values[(52, 0, 0, 0)][140] == 1, "Input EQ bypass uses function 36 and confirms the aggregate flag");
        var beforeEnable = peer.Values[(52, 0, 0, 0)].ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, EqEnabled: true)).Id);
        check(peer.Values[(36, 0, 0, 0)][0] == 0 && peer.Values[(52, 0, 0, 0)][..140].SequenceEqual(beforeEnable[..140]) && peer.Values[(52, 0, 0, 0)][140] == 0, "EQ-only enable preserves every filter record");
        peer.IgnoreEqBypassAggregate = true;
        try { service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, EqEnabled: false)).Id); throw new Exception("EQ bypass mismatch accepted"); }
        catch (InvalidOperationException) { check(true, "EQ bypass without matching aggregate readback fails"); }
        finally { peer.IgnoreEqBypassAggregate = false; }
        var beforeFrequency = service.GetInputProcessing(id, 1);
        var beforeFrequencyRaw = peer.Values[(52, 0, 0, 0)].ToArray();
        var frequencyBands = beforeFrequency.Bands.Select(b => b.Number == 5 ? b with { FrequencyHz = 1250 } : b).ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: frequencyBands)).Id);
        var expectedFrequencyRaw = beforeFrequencyRaw.ToArray(); BitConverter.GetBytes(1250f).CopyTo(expectedFrequencyRaw, 5 * 14 + 5);
        check(BitConverter.ToSingle(peer.Values[(32, 0, 5, 0)]) == 1250 && peer.Values[(52, 0, 0, 0)].SequenceEqual(expectedFrequencyRaw), "Frequency-only edit uses function 32 and preserves every unrelated filter byte");
        check(service.GetInputProcessing(id, 2).Bands[4].FrequencyHz == 100, "Frequency edit isolates other inputs");
        peer.IgnoreEqFrequencyAggregate = true;
        try { service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeFrequency.Bands)).Id); throw new Exception("EQ frequency mismatch accepted"); }
        catch (InvalidOperationException) { check(true, "Targeted frequency write without aggregate readback fails"); }
        finally { peer.IgnoreEqFrequencyAggregate = false; }
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeFrequency.Bands)).Id);
        var beforeBandGain = service.GetInputProcessing(id, 1);
        var beforeBandGainRaw = peer.Values[(52, 0, 0, 0)].ToArray();
        var gainBands = beforeBandGain.Bands.Select(b => b.Number == 5 ? b with { GainDb = 4.5 } : b).ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: gainBands)).Id);
        var expectedGainRaw = beforeBandGainRaw.ToArray(); BitConverter.GetBytes(4.5f).CopyTo(expectedGainRaw, 5 * 14 + 1);
        check(BitConverter.ToSingle(peer.Values[(31, 0, 5, 0)]) == 4.5 && peer.Values[(52, 0, 0, 0)].SequenceEqual(expectedGainRaw), "Gain-only edit uses function 31 and preserves frequency, Q and other filter bytes");
        check(service.GetInputProcessing(id, 2).Bands[4].GainDb == 0, "Band gain edit isolates other input channels");
        peer.IgnoreEqGainAggregate = true;
        try { service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeBandGain.Bands)).Id); throw new Exception("Band gain mismatch accepted"); }
        catch (InvalidOperationException) { check(true, "Targeted band gain without aggregate readback fails"); }
        finally { peer.IgnoreEqGainAggregate = false; }
        var combinedBands = beforeBandGain.Bands.Select(b => b.Number == 5 ? b with { GainDb = 5, FrequencyHz = 1200, Q = 4 } : b).ToArray();
        var combinedResult = service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: combinedBands)).Id);
        check(combinedResult.Bands[4] is { GainDb: 5, FrequencyHz: 1200, Q: 4 } && BitConverter.ToSingle(peer.Values[(31, 0, 5, 0)]) == 5 && BitConverter.ToSingle(peer.Values[(32, 0, 5, 0)]) == 1200, "Combined gain/frequency/Q edit verifies each targeted field in sequence");
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeBandGain.Bands)).Id);
        var beforeQ = service.GetInputProcessing(id, 1);
        var beforeQRaw = peer.Values[(52, 0, 0, 0)].ToArray();
        var qBands = beforeQ.Bands.Select(b => b.Number == 5 ? b with { Q = 3.5 } : b).ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: qBands)).Id);
        var expectedQRaw = beforeQRaw.ToArray(); BitConverter.GetBytes(3.5f).CopyTo(expectedQRaw, 5 * 14 + 9);
        check(BitConverter.ToSingle(peer.Values[(34, 0, 5, 0)]) == 3.5 && peer.Values[(52, 0, 0, 0)].SequenceEqual(expectedQRaw), "Q-only edit uses function 34 and preserves gain, frequency and filter bytes");
        check(service.GetInputProcessing(id, 2).Bands[4].Q == 1, "Q edit isolates other input channels");
        peer.IgnoreEqQAggregate = true;
        try { service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeQ.Bands)).Id); throw new Exception("Q mismatch accepted"); }
        catch (InvalidOperationException) { check(true, "Targeted Q write without aggregate readback fails"); }
        finally { peer.IgnoreEqQAggregate = false; }
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeQ.Bands)).Id);
        var beforeBandBypass = service.GetInputProcessing(id, 1);
        var beforeBandBypassRaw = peer.Values[(52, 0, 0, 0)].ToArray();
        var bypassBands = beforeBandBypass.Bands.Select(b => b.Number == 5 ? b with { Enabled = false } : b).ToArray();
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: bypassBands)).Id);
        var expectedBandBypassRaw = beforeBandBypassRaw.ToArray(); expectedBandBypassRaw[5 * 14] = 255;
        check(peer.Values[(30, 0, 5, 0)][0] == 255 && peer.Values[(52, 0, 0, 0)].SequenceEqual(expectedBandBypassRaw), "Peak bypass uses function 30 and preserves all other filter bytes");
        peer.IgnoreEqBandBypassAggregate = true;
        try { service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeBandBypass.Bands)).Id); throw new Exception("Band bypass mismatch accepted"); }
        catch (InvalidOperationException) { check(true, "Peak bypass without matching aggregate readback fails"); }
        finally { peer.IgnoreEqBandBypassAggregate = false; }
        service.ApplyInputProcessing(service.PlanInputProcessing(new(id, 1, Bands: beforeBandBypass.Bands)).Id);
        check(peer.Values[(30, 0, 5, 0)][0] == 0 && service.GetInputProcessing(id, 1).Bands[4].Enabled, "Peak enable restores the original filter with readback");
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
        check(peer.Values[(48,0,0,1)][0]==1,"Automatic RMS timing sends the physically captured function-48 value 1");
        remoteService.ApplyOutputDynamics(remoteService.PlanOutputDynamics(new(id,1,Rms:rmsState with {Automatic=false})).Id);
        check(peer.Values[(48,0,0,1)][0]==0 && !service.GetOutputDynamics(id,1).Rms.Automatic,"Manual RMS timing sends function-48 value 0 and agrees with aggregate readback");
        remoteService.ApplyOutputDynamics(remoteService.PlanOutputDynamics(new(id,1,Rms:rmsState)).Id);
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
        try{service.RenameDevice(id,new string('x',32));throw new Exception("Legacy backend accepted a 32-byte physical name");}
        catch(ArgumentException){check(service.GetDevice(id).Name=="Bench amp","Legacy physical name validation rejects 32 bytes and preserves the device name");}
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
        public volatile bool IgnoreWrites, DropReplies, IgnoreDynamicsAggregate, IgnoreEqBypassAggregate, IgnoreEqFrequencyAggregate, IgnoreEqGainAggregate, IgnoreEqQAggregate, IgnoreEqBandBypassAggregate;
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
                Values[(9,ch,0,0)]=BitConverter.GetBytes(0f);
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
                eq[5 * 14] = 0; // Captured Peak type for the targeted bypass fixture.
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
                                if (function == 30 && !IgnoreEqBandBypassAggregate)
                                {
                                    var filters = Values[(52, channel, 0, side)].ToArray();
                                    filters[segment * 14] = request[0];
                                    Values[(52, channel, 0, side)] = filters;
                                }
                                if (function == 34 && !IgnoreEqQAggregate)
                                {
                                    var filters = Values[(52, channel, 0, side)].ToArray();
                                    request.CopyTo(filters, segment * 14 + 9);
                                    Values[(52, channel, 0, side)] = filters;
                                }
                                if (function == 31 && !IgnoreEqGainAggregate)
                                {
                                    var filters = Values[(52, channel, 0, side)].ToArray();
                                    request.CopyTo(filters, segment * 14 + 1);
                                    Values[(52, channel, 0, side)] = filters;
                                }
                                if (function == 32 && !IgnoreEqFrequencyAggregate)
                                {
                                    var filters = Values[(52, channel, 0, side)].ToArray();
                                    request.CopyTo(filters, segment * 14 + 5);
                                    Values[(52, channel, 0, side)] = filters;
                                }
                                if (function == 36 && !IgnoreEqBypassAggregate)
                                {
                                    var filters = Values[(52, channel, 0, side)].ToArray();
                                    filters[140] = request[0];
                                    Values[(52, channel, 0, side)] = filters;
                                }
                                var rmsMap=new Dictionary<byte,int>{{37,0},{38,2},{39,3},{46,7},{48,8}};var peakMap=new Dictionary<byte,int>{{40,0},{41,2},{42,4},{47,8}};
                                if(!IgnoreDynamicsAggregate && rmsMap.TryGetValue(function,out var ro)){var raw=Values[(55,channel,0,1)].ToArray();request.CopyTo(raw,ro);Values[(55,channel,0,1)]=raw;}
                                if(!IgnoreDynamicsAggregate && peakMap.TryGetValue(function,out var po)){var raw=Values[(54,channel,0,1)].ToArray();request.CopyTo(raw,po);Values[(54,channel,0,1)]=raw;}
                            }
                        }
                        continue;
                    }
                    if(state==0)
                    {
                        if(function is 15 or 49 && !IgnoreWrites) Values[key]=request;
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
