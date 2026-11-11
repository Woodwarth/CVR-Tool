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
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, TextBlock> deviceDots = new();
    private bool refreshingLive;
    private DateTimeOffset preserveStatusUntil;
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
                if (!liveReadOnly || OwnedWindows.Count != 0 || controlWritePending || selected != deviceId) return;
                var snapshot = devices.SingleOrDefault(d => d.Id == selected);
                if (snapshot is null) return;
                channels = snapshot.Outputs.Select(c => new ChannelDraft(c)).ToList();
                Render();
                var readings = snapshot.Readings!;
                if (DateTimeOffset.UtcNow >= preserveStatusUntil) status.Text = readings.Error is null ? $"Live · Limited editing · Settings {readings.SettingsObservedAt.ToLocalTime():HH:mm:ss} · Status {readings.TelemetryObservedAt?.ToLocalTime():HH:mm:ss}" :
                    "Connection unavailable · Last received values shown · " + readings.Error;
            }
            catch (Exception ex) { status.Text = "Live refresh failed: " + ex.Message; }
            finally { refreshingLive = false; }
        };
        liveTimer.Start();
        Closed += (_, _) => liveTimer.Stop();
    }
    private async void SetDeviceWink(bool enabled)
    {
        if (controlWritePending) return;
        controlWritePending = true;
        var selected = deviceId;
        deviceActions.IsEnabled = false;
        status.Text = "Applying Wink and checking device state…";
        try
        {
            var after = await Task.Run(() => service.SetWink(selected, enabled));
            status.Text = service.GetDevice(selected).IsDemo ? "Demo Wink " + (after.Wink == true ? "on" : "off") : "Device reports Wink " + (after.Wink == true ? "on" : "off");
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { controlWritePending = false; preserveStatusUntil = DateTimeOffset.UtcNow.AddSeconds(5); Render(); }
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
        finally { controlWritePending = false; preserveStatusUntil = DateTimeOffset.UtcNow.AddSeconds(5); Render(); }
    }
    private async void CommitLiveMute(ChannelChange change)
    {
        await RunLiveMute(change.DeviceId, () => service.Apply(service.Plan(change).Id));
    }
    private async void CommitLiveRouteMute(int input, int output, bool muted)
    {
        var selected = deviceId;
        await RunLiveMute(selected, () => service.ApplyRouting(service.PlanRouting(new(selected, input, output, Muted: muted)).Id));
    }
    private async Task RunLiveMute(string selected, Action change)
    {
        if (controlWritePending) return;
        controlWritePending = true; Render();
        status.Text = "Applying mute and checking device readback…";
        try { await Task.Run(change); status.Text = "Mute confirmed by device readback"; }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            controlWritePending = false;
            preserveStatusUntil = DateTimeOffset.UtcNow.AddSeconds(5);
            try
            {
                if (selected == deviceId) channels = service.GetDevice(selected).Outputs.Select(c => new ChannelDraft(c)).ToList();
                Render();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
    }
    private static void DisableLiveEdits(Control root, bool editor, bool canMute = false)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
        {
            if (control is RoutingDial dial)
            { dial.CanEditGain = false; dial.IsEnabled = dial.IsEnabled && canMute; }
            else if (control is ToggleButton toggle && (toggle.Tag?.ToString()?.StartsWith("mute-") ?? false))
                toggle.IsEnabled = toggle.IsEnabled && canMute;
            else if (control is Slider or NumericUpDown or TextBox or ToggleButton or GainReadout or EqualizerGraph)
                control.IsEnabled = false;
            if (control is Button button)
            {
                var tag = button.Tag?.ToString() ?? "";
                var allowed = editor ? button.Content?.ToString() is "Cancel" or "Close" :
                    tag.StartsWith("input-eq-") || tag.StartsWith("output-eq-") || tag == "snapshot-save-pc" || (canMute && tag.StartsWith("mute-"));
                if (!allowed) button.IsEnabled = false;
            }
        }
    }
}
