// Application / Main input controls and device name
// Purpose: apply captured input gain/mute/delay/Peak-EQ commands and identity names.
// Units: gain float32 dB, active-low mute and 32-byte ASCII names.
// State: fresh settings protect revisions; each write requires independent readback.
// Scope: only BW-12/BW-24 input crossovers and Peak EQ; no automatic retries or rollback.
using System.Diagnostics;
using System.Text;
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change, long expectedRevision)
    {
        MutateSettings(change.DeviceId, expectedRevision, client =>
        {
            CheckChannel(change.Channel);
            var before = settings.Inputs!.Single(i => i.Channel == change.Channel);
            var after = AmplifierService.MergeInput(before, change);
            // Validate both filters before any field is sent, including unrelated input edits.
            foreach (var (prior, next) in new[] { (before.HighPass, after.HighPass), (before.LowPass, after.LowPass) })
                if (next != prior && (!ObservedInputCrossovers.CanWrite(prior) || !ObservedInputCrossovers.CanWrite(next)))
                    throw new NotSupportedException("Only evidenced input BW-12/BW-24 crossovers are writable.");
            foreach (var band in after.Bands)
                if (band != before.Bands.Single(b => b.Number == band.Number) && band.FilterType != 0)
                    throw new NotSupportedException("Only physically evidenced input Peak filters are writable.");
            var ch = (byte)(change.Channel - 1); var offset = 36 + (change.Channel - 1) * 515;
            if (change.GainDb is { } gain) WriteSettingsBytes(client, 9, ch, 0, offset + 369, BitConverter.GetBytes((float)gain));
            if (change.Muted is { } muted) WriteSettingsBytes(client, 10, ch, 0, 2192 + change.Channel - 1, [(byte)(muted ? 0 : 1)]);
            if (change.DelayMs is { } delay) WriteSettingsBytes(client, 14, ch, 0, offset + 50, BitConverter.GetBytes((float)delay));
            WriteInputCrossover(client, ch, offset, 0, before.HighPass, after.HighPass);
            WriteInputCrossover(client, ch, offset, 9, before.LowPass, after.LowPass);
            foreach (var band in after.Bands)
            {
                var prior = before.Bands.Single(b => b.Number == band.Number); var bandOffset = offset + 85 + band.Number * 14;
                if (band.Enabled != prior.Enabled) WriteSettingsBytes(client, 30, ch, 0, bandOffset, [(byte)(band.Enabled ? 0 : 255)], segment:(byte)band.Number);
                if (band.GainDb != prior.GainDb) WriteSettingsBytes(client, 31, ch, 0, bandOffset + 1, BitConverter.GetBytes((float)band.GainDb), segment:(byte)band.Number);
                if (band.FrequencyHz != prior.FrequencyHz) WriteSettingsBytes(client, 32, ch, 0, bandOffset + 5, BitConverter.GetBytes((float)band.FrequencyHz), segment:(byte)band.Number);
                if (band.Q != prior.Q) WriteSettingsBytes(client, 34, ch, 0, bandOffset + 9, BitConverter.GetBytes((float)band.Q), segment:(byte)band.Number);
            }
            if (change.EqEnabled is { } eq) WriteSettingsBytes(client, 36, ch, 0, offset + 225, [(byte)(eq ? 0 : 1)]);
        });
        return GetInputProcessing(change.DeviceId, change.Channel).State;
    }
    private void WriteInputCrossover(CvrUdpClient client, byte channel, int record, byte slot,
        CrossoverFilter? before, CrossoverFilter? after)
    {
        if (before is null || after is null || before == after) return;
        var offset = record + 85 + slot * 14;
        if (after.FrequencyHz != before.FrequencyHz)
            WriteSettingsBytes(client, 32, channel, 0, offset + 5,
                BitConverter.GetBytes((float)after.FrequencyHz), segment: slot);
        if (after.FilterType != before.FilterType || after.Enabled != before.Enabled)
            WriteSettingsBytes(client, 30, channel, 0, offset,
                [unchecked((byte)(after.Enabled ? after.FilterType : -after.FilterType - 1))], segment: slot);
    }

    private void WriteSettingsBytes(CvrUdpClient client, byte function, byte channel, byte side, int offset, byte[] body, byte segment = 0, byte messageStatus = 1)
    {
        byte[] expected;
        lock (gate)
        {
            expected = native.ToArray();
            if (expected.AsSpan(offset, body.Length).SequenceEqual(body)) return;
            body.CopyTo(expected, offset); revision++;
        }
        client.Write(function, body, channel, segment, side, messageStatus);
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            ReadSettings(client);
            lock (gate) if (native.AsSpan(offset, body.Length).SequenceEqual(body)) break;
            if (deadline.Elapsed >= TimeSpan.FromSeconds(2)) throw new InvalidOperationException("Settings readback did not confirm the requested value.");
            Thread.Sleep(75);
        }
        lock (gate) if (!native.SequenceEqual(expected)) throw new InvalidOperationException("Other settings changed during editing; current readings were refreshed.");
    }
    public DeviceSnapshot Rename(string deviceId, string name, long expectedRevision)
    {
        name = AmplifierService.NormalizeDeviceName(name);
        if (name.Any(c => c > 127)) throw new ArgumentException("Device-side names require ASCII characters (up to 32 bytes).");
        lock (ioGate)
        {
            lock (gate) CheckId(deviceId);
            try
            {
                CvrEndpointGate.UseClient(target, client =>
                {
                    ReadSettings(client);
                    var beforeIdentity = client.ReadSnapshot(0);
                    byte[] expected;
                    lock (gate)
                    {
                        if (!beforeIdentity[..32].SequenceEqual(native[..32]) ||
                            (identityBody is { } known && !known[64..70].SequenceEqual(beforeIdentity[64..70])))
                            throw new NotSupportedException("Device identity changed; reconnect before renaming.");
                        if (identityBody is { } prior && Text(prior[32..64]) != Text(beforeIdentity[32..64])) revision++;
                        identityBody = beforeIdentity;
                        if (revision != expectedRevision) throw new InvalidOperationException("Device changed. Read current settings before renaming.");
                        expected = native.ToArray();
                    }
                    var body = new byte[32]; Encoding.ASCII.GetBytes(name).CopyTo(body, 0);
                    if (Text(beforeIdentity[32..64]) != name)
                    {
                        lock (gate) revision++;
                        client.Write(60, body);
                    }
                    var afterIdentity = client.ReadSnapshot(0);
                    lock (gate) identityBody = afterIdentity;
                    ReadSettings(client);
                    if (Text(afterIdentity[32..64]) != name || !afterIdentity[..32].SequenceEqual(beforeIdentity[..32]) ||
                        !afterIdentity[64..70].SequenceEqual(beforeIdentity[64..70]))
                        throw new InvalidOperationException("Device name readback did not confirm the requested identity.");
                    lock (gate) if (!native.SequenceEqual(expected)) throw new InvalidOperationException("Other settings changed while renaming; readings were refreshed.");
                });
                lock (gate) { error = null; UpdateSnapshot(); return CopyDevice(); }
            }
            catch (Exception ex)
            { lock (gate) { forceSettingsRefresh = true; error = ex.Message; UpdateSnapshot(); } throw; }
        }
    }
}
