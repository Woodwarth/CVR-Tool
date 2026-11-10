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
        public volatile bool IgnoreWrites, DropReplies;
        public Peer()
        {
            var basic = new byte[75];
            Encoding.ASCII.GetBytes("Synthetic DSP-2002").CopyTo(basic, 0);
            Encoding.ASCII.GetBytes("Test amp").CopyTo(basic, 32);
            basic[71] = basic[73] = 2;
            Values[(0, 0, 0, 0)] = basic;
            Values[(15, 0, 0, 0)] = [0];
            Values[(17, 0, 0, 0)] = [0];
            for (byte ch = 0; ch < 2; ch++)
            {
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
                for (byte output = 0; output < 2; output++)
                    Values[(12, ch, output, 0)] = [0, 0, 0, 0, 1];
            }
            var telemetry = new byte[32];
            BitConverter.GetBytes(40f).CopyTo(telemetry, 4);
            BitConverter.GetBytes(5f).CopyTo(telemetry, 12);
            telemetry[20] = 5;
            Values[(6, 0, 0, 0)] = telemetry;
            listener = Task.Run(Listen);
        }
        private async Task Listen()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(stop.Token);
                    var packet = received.Buffer;
                    if (packet.Length < 23 || packet[8] == 1)
                        continue;
                    byte function = packet[11], state = packet[12], channel = packet[13], segment = packet[14], side = packet[19];
                    var key = (function, channel, segment, side);
                    if (state == 1)
                    {
                        if (!IgnoreWrites)
                        {
                            if (function == 60)
                            {
                                var basic = Values[(0, 0, 0, 0)].ToArray();
                                packet.AsSpan(20, 32).CopyTo(basic.AsSpan(32));
                                Values[(0, 0, 0, 0)] = basic;
                            }
                            else
                                Values[key] = packet[20..^3];
                        }
                        continue;
                    }
                    if (DropReplies || !Values.TryGetValue(key, out var body))
                        continue;
                    // Independent fixture builder, deliberately not using production Encode.
                    var reply = new byte[23 + body.Length];
                    reply[0] = 3;
                    reply[1] = 217;
                    reply[2] = 202;
                    reply[4] = reply[7] = 1;
                    reply[9] = 0x21;
                    BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(5), (ushort)(13 + body.Length));
                    reply[10] = 85;
                    reply[11] = function;
                    reply[13] = channel;
                    reply[14] = segment;
                    reply[19] = side;
                    body.CopyTo(reply, 20);
                    BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(reply.Length - 3), (ushort)(13 + body.Length));
                    reply[^1] = unchecked((byte)reply.Skip(10).Take(reply.Length - 11).Sum(b => b));
                    await socket.SendAsync(reply, received.RemoteEndPoint, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
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
