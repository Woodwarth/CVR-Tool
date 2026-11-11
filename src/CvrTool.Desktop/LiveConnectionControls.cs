// Desktop / Automatic live device view
// Purpose: refresh connected devices and show connection dots beside their names.
// Units: UTC observation timestamps; no uncalibrated level conversion.
// State: offline devices retain last readings; demos remain editable local sessions.
// Scope: automatic discovery owns connections; this view has no manual buttons.
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, TextBlock> deviceDots = new();
    private bool refreshingLive;
    private static IBrush ConnectionColor(DeviceSnapshot device)
    {
        var connected = device.IsDemo || (device.Readings is { Error: null, TelemetryObservedAt: { } at } &&
            DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(3));
        return Brush.Parse(connected ? "#00CE63" : "#F05462");
    }
    private void StartLiveRefresh()
    {
        liveTimer.Tick += async (_, _) =>
        {
            if (refreshingLive) return;
            refreshingLive = true;
            var selected = deviceId;
            try
            {
                var devices = await Task.Run(() => service.ListDevices());
                foreach (var device in devices)
                    if (deviceDots.TryGetValue(device.Id, out var dot)) dot.Foreground = ConnectionColor(device);
                if (!liveReadOnly || OwnedWindows.Count != 0 || selected != deviceId) return;
                var snapshot = devices.SingleOrDefault(d => d.Id == selected);
                if (snapshot is null) return;
                channels = snapshot.Outputs.Select(c => new ChannelDraft(c)).ToList();
                Render();
                var readings = snapshot.Readings!;
                status.Text = readings.Error is null ? $"Live · DSP read-only · Settings {readings.SettingsObservedAt.ToLocalTime():HH:mm:ss} · Status {readings.TelemetryObservedAt?.ToLocalTime():HH:mm:ss}" :
                    "Connection unavailable · Last received values shown · " + readings.Error;
            }
            catch (Exception ex) { status.Text = "Live refresh failed: " + ex.Message; }
            finally { refreshingLive = false; }
        };
        liveTimer.Start();
        Closed += (_, _) => liveTimer.Stop();
    }
    private async void SetLiveControls(bool? locked, bool? standby)
    {
        if (controlWritePending) return;
        controlWritePending = true;
        var selected = deviceId;
        deviceActions.IsEnabled = false;
        status.Text = "Applying operational control and checking device readback…";
        try
        {
            var after = await Task.Run(() => service.SetControlState(selected, locked, standby));
            if (selected == deviceId) status.Text = $"Confirmed · Display {(after.DisplayLocked ? "locked" : "unlocked")} · Standby {(after.Standby ? "on" : "off")}";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { controlWritePending = false; Render(); }
    }
    private static void DisableLiveEdits(Control root, bool editor)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
        {
            if (control is Slider or NumericUpDown or TextBox or ToggleButton or GainReadout or RoutingDial or EqualizerGraph)
                control.IsEnabled = false;
            if (control is Button button)
            {
                var tag = button.Tag?.ToString() ?? "";
                var allowed = editor ? button.Content?.ToString() is "Cancel" or "Close" :
                    tag.StartsWith("input-eq-") || tag.StartsWith("output-eq-") || tag == "snapshot-save-pc";
                if (!allowed) button.IsEnabled = false;
            }
        }
    }
}
