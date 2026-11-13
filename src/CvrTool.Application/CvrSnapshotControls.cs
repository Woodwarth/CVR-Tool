// Application / Verified operational controls
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: allow display lock and standby while leaving DSP settings read-only.
// Units: display function 17/status 1; standby function 15/status 0; bytes 0/1.
// State: each attempted write invalidates plans and requires a fresh full sync.
// Scope: no automatic retries or rollback; unrelated native changes are rejected.
using System.Diagnostics;
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    public DeviceControlState SetControlState(string deviceId, bool? locked, bool? standby, long expectedRevision)
    {
        if (locked is null && standby is null) throw new ArgumentException("Supply display lock or standby state.");
        lock (ioGate)
        {
            lock (gate) CheckId(deviceId);
            try
            {
                CvrEndpointGate.UseClient(target, client =>
                {
                    ReadSettings(client);
                    ReadTelemetry(client);
                    lock (gate)
                        if (revision != expectedRevision) throw new InvalidOperationException("Device changed. Read current state before retrying.");
                    if (locked is { } display) WriteControl(client, 17, 33, display, 1);
                    if (standby is { } powerState) WriteStandby(client, powerState);
                });
                lock (gate)
                {
                    error = null; UpdateSnapshot();
                    return new(native[33] != 0, standbyState, StandbyKnown: standbyKnown, Wink: winkState);
                }
            }
            catch (Exception ex)
            {
                lock (gate) { forceSettingsRefresh = true; error = ex.Message; UpdateSnapshot(); }
                throw;
            }
        }
    }
    private void WriteControl(CvrUdpClient client, byte function, int nativeOffset, bool enabled, byte messageStatus)
    {
        byte[] expected;
        lock (gate)
        {
            expected = native.ToArray();
            if (expected[nativeOffset] == (enabled ? 1 : 0)) return;
            revision++;
        }
        expected[nativeOffset] = (byte)(enabled ? 1 : 0);
        client.Write(function, [expected[nativeOffset]], messageStatus: messageStatus);
        ReadSettings(client);
        lock (gate)
            if (!native.SequenceEqual(expected))
                throw new InvalidOperationException("Control readback differs or other settings changed. Current readings were refreshed; no automatic retry was made.");
    }
    private void WriteStandby(CvrUdpClient client, bool enabled)
    {
        byte[] expected;
        lock (gate)
        {
            if (!standbyKnown) throw new NotSupportedException("Standby telemetry is not known on this device.");
            if (standbyState == enabled) return;
            expected = native.ToArray(); revision++;
        }
        client.Write(15, [(byte)(enabled ? 1 : 0)], messageStatus: 0);
        // Telemetry confirms the operational transition first; native byte 32
        // follows asynchronously. Repeat only reads, never the setting command.
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            ReadTelemetry(client);
            lock (gate) if (standbyKnown && standbyState == enabled) break;
            if (deadline.Elapsed >= TimeSpan.FromSeconds(6)) throw new InvalidOperationException("Standby telemetry did not confirm the requested state.");
            Thread.Sleep(75);
        }
        expected[32] = (byte)(enabled ? 1 : 0);
        while (true)
        {
            ReadSettings(client);
            lock (gate) if (native[32] == expected[32]) break;
            if (deadline.Elapsed >= TimeSpan.FromSeconds(6)) throw new InvalidOperationException("Standby settings readback did not settle to the requested state.");
            Thread.Sleep(75);
        }
        lock (gate)
            if (!native.SequenceEqual(expected)) throw new InvalidOperationException("Other settings changed during standby switching; current readings were refreshed.");
    }
}
