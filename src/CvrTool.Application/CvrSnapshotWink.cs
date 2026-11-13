// Application / Amplifier identification
// Module: Coordinates device sessions, validates DSP changes and reads native presets.
// Purpose: blink the physical display without changing audio processing.
// Units: function 83/status 0 uses one byte, 0 off or 1 on.
// State: bulk-shaped function-83 queries independently confirm each change.
// Scope: settings stay byte-identical; no automatic command retries or rollback.
using CvrTool.Core;
using CvrTool.Protocol;

namespace CvrTool.Application;

public sealed partial class CvrSnapshotBackend
{
    private bool? winkState;
    private DateTimeOffset winkCheckedAt;

    private static bool DecodeWink(byte[] body) => body is [0] ? false : body is [1] ? true :
        throw new NotSupportedException("Unknown Wink state.");

    private void ReadWink(CvrUdpClient client)
    {
        try { var state = DecodeWink(client.ReadSnapshot(83)); lock (gate) winkState = state; }
        catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException)
        { lock (gate) winkState = null; }
        finally { winkCheckedAt = DateTimeOffset.UtcNow; }
    }

    public DeviceControlState SetWink(string deviceId, bool enabled, long expectedRevision)
    {
        lock (ioGate)
        {
            lock (gate) CheckId(deviceId);
            try
            {
                CvrEndpointGate.UseClient(target, client =>
                {
                    ReadSettings(client);
                    var beforeWink = DecodeWink(client.ReadSnapshot(83));
                    byte[] expected;
                    lock (gate)
                    {
                        winkState = beforeWink;
                        if (revision != expectedRevision) throw new InvalidOperationException("Device changed. Read current settings before retrying Wink.");
                        expected = native.ToArray();
                        if (beforeWink != enabled) { revision++; winkState = null; }
                    }
                    if (beforeWink != enabled) client.Write(83, [(byte)(enabled ? 1 : 0)], messageStatus: 0);
                    var observed = DecodeWink(client.ReadSnapshot(83));
                    lock (gate) { winkState = observed; winkCheckedAt = DateTimeOffset.UtcNow; }
                    ReadSettings(client);
                    lock (gate)
                    {
                        if (observed != enabled) throw new InvalidOperationException("Wink readback did not confirm the requested state.");
                        if (!native.SequenceEqual(expected)) throw new InvalidOperationException("Other settings changed during Wink; current settings were refreshed.");
                    }
                });
                lock (gate) { error = null; UpdateSnapshot(); return GetControlState(deviceId); }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    if (ex is TimeoutException or IOException or NotSupportedException) winkState = null;
                    forceSettingsRefresh = true; error = ex.Message; UpdateSnapshot();
                }
                throw;
            }
        }
    }
}
