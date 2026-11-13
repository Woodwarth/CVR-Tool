// Desktop / Automatic live device view
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
    private long renderedRevision;
    private bool renderedOnline;
    private DeviceReadings? renderedReadings;
    private bool refreshingLive;

    private void UpdateMainTelemetry()
    {
        var tagged = page.GetLogicalDescendants().OfType<Control>().Where(c => c.Tag is string)
            .ToLookup(c => c.Tag!.ToString()!);
        void Light(string tag, bool? value)
        {
            if (tagged[tag].FirstOrDefault() is not StackPanel panel) return;
            panel.Children.OfType<StatusLed>().FirstOrDefault()?.Update(value);
            if (panel.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            {
                label.Foreground = value == true ? Brushes.LightCoral : Muted;
                ToolTip.SetTip(panel,label.Text + ": " + (value is null ? "Unknown" : value.Value ? "Active" : "Inactive"));
            }
        }
        foreach (var t in service.GetTelemetry(deviceId))
        {
            var name = (char)('A' + t.Channel - 1);
            (tagged[$"channel-meter-{name}"].FirstOrDefault() as ChannelMeter)?.Update(t.InputMeterDb);
            (tagged[$"output-meter-{name}"].FirstOrDefault() as ChannelMeter)?.Update(t.OutputMeterDb);
            Light($"channel-clip-{name}",t.InputClip);
            var s = t.Status;
            bool?[] flags = [s?.Fault,s?.Load,s?.Open,s?.OverTemperature,s?.Clip,s?.Standby,s?.HighImpedance,s?.Bridged,s?.Limiting];
            for (int i=0;i<flags.Length;i++) Light($"output-status-{name}-{i}",flags[i]);
            double?[] values = [t.Voltage,t.Current,t.LoadOhms,t.TemperatureC];
            for (int i=0;i<values.Length;i++)
                if(tagged[$"telemetry-{name}-{i}"].FirstOrDefault() is TextBlock field)
                    field.Text = values[i]?.ToString("0.0") ?? "—";
        }
    }
    private int liveEditInteractions;
    private bool editingDeviceName;
    private DateTimeOffset preserveStatusUntil;
    private static bool ConnectionAvailable(DeviceSnapshot device) => device.IsDemo ||
        (device.Readings is { Error: null, TelemetryObservedAt: { } at } && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(3));
    private static IBrush ConnectionColor(DeviceSnapshot device) => Brush.Parse(ConnectionAvailable(device) ? "#00CE63" : "#F05462");
    private void StartLiveRefresh()
    {
        liveTimer.Tick += async (_, _) => await RefreshLiveAsync();
        liveTimer.Start();
        Closed += (_, _) => liveTimer.Stop();
    }

    public async Task RefreshLiveAsync()
    {
        if (refreshingLive) return;
        refreshingLive = true;
        var selected = deviceId;
        try
        {
            var devices = await Task.Run(() => service.ListDevices());
            foreach (var device in devices)
                if (deviceDots.TryGetValue(device.Id, out var dot)) dot.Foreground = ConnectionColor(device);
            if (!liveReadOnly || (activeTab == "Presets" && (page.GetLogicalDescendants().OfType<ComboBox>().Any(box => box.IsDropDownOpen) || page.GetLogicalDescendants().OfType<TextBox>().Any(box => box.IsKeyboardFocusWithin))) || OwnedWindows.Count != 0 || controlWritePending || liveEditInteractions > 0 || editingDeviceName || selected != deviceId) return;
            var snapshot = devices.SingleOrDefault(d => d.Id == selected);
            if (snapshot is null) return;
            var metadata = snapshot.Readings!;
            if (renderedRevision != service.SettingsRevision(selected) || renderedOnline != ConnectionAvailable(snapshot) ||
                renderedReadings?.CanSetWink != metadata.CanSetWink || renderedReadings?.CanSetStandby != metadata.CanSetStandby ||
                (nameSlot.Content as Button)?.Content is TextBlock name && name.Text != snapshot.Name)
            {
                channels = snapshot.Outputs.Select(c => new ChannelDraft(c)).ToList();
                Render();
            }
            else
            {
                RefreshDeviceInfo();
                if (activeTab == "Main") UpdateMainTelemetry();
            }
            var readings = snapshot.Readings!;
            if (DateTimeOffset.UtcNow >= preserveStatusUntil) status.Text = readings.Error is null ? $"Live · Verified editing · Settings {readings.SettingsObservedAt.ToLocalTime():HH:mm:ss} · Status {readings.TelemetryObservedAt?.ToLocalTime():HH:mm:ss}" :
                "Connection unavailable · Last received values shown · " + readings.Error;
        }
        catch (Exception ex) { status.Text = "Live refresh failed: " + ex.Message; }
        finally { refreshingLive = false; }
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
    private async void CommitLiveRouteMute(int input, int output, bool? muted, double? gainDb = null)
    {
        var selected = deviceId;
        await RunLiveChange(selected, () => service.ApplyRouting(service.PlanRouting(new(selected, input, output, GainDb: gainDb, Muted: muted)).Id), gainDb is null ? "Routing mute" : "Routing gain");
    }
    private Task RunLiveMute(string selected, Action change) => RunLiveChange(selected, change, "Mute");
    private Task CommitLiveInput(CvrTool.Core.InputProcessingChange change, string operation) =>
        RunLiveChange(change.DeviceId, () => service.ApplyInputProcessing(service.PlanInputProcessing(change).Id), operation);
    private async Task RunLiveChange(string selected, Action change, string operation)
    {
        if (controlWritePending) return;
        controlWritePending = true; Render();
        status.Text = "Applying " + operation + " and checking device readback…";
        try { await Task.Run(change); status.Text = operation + " confirmed by device readback"; }
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
    private static void DisableLiveEdits(Control root, bool editor, bool canMute = false, bool canInputGain = false, bool canVerifiedDsp = false)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
        {
            if (editor && control.Tag is "generate-eq-fir" or "fir-export" or "editor-channel") continue;
            if (control is RoutingDial dial)
            { dial.CanEditGain = canVerifiedDsp; dial.IsEnabled = dial.IsEnabled && (canMute || canVerifiedDsp); }
            else if (control is ToggleButton toggle && (toggle.Tag?.ToString()?.StartsWith("mute-") ?? false))
                toggle.IsEnabled = toggle.IsEnabled && canMute;
            else if (control is Slider slider && slider.Tag?.ToString()?.StartsWith("input-gain-") == true)
                slider.IsEnabled = slider.IsEnabled && canInputGain;
            else if (control is GainReadout readout && (readout.Tag?.ToString()?.StartsWith("output-gain-") == true || readout.Tag?.ToString()?.StartsWith("routing-output-gain-") == true))
                readout.IsEnabled = readout.IsEnabled && canVerifiedDsp;
            else if (control is Slider or NumericUpDown or ComboBox or TextBox or ToggleButton or GainReadout or EqualizerGraph)
                control.IsEnabled = false;
            if (control is Button button)
            {
                var tag = button.Tag?.ToString() ?? "";
                var allowed = editor ? button.Content?.ToString() is "Cancel" or "Close" :
                    (canVerifiedDsp && tag == "gain-edit" && button.GetLogicalAncestors().OfType<GainReadout>().Any(r => r.IsEnabled)) ||
                    tag.StartsWith("input-eq-") || tag.StartsWith("output-eq-") ||
                    tag.StartsWith("input-delay-") || tag.StartsWith("output-delay-") ||
                    tag.StartsWith("output-mode-") || tag.StartsWith("output-noise-") ||
                    tag.StartsWith("output-source-") || tag.StartsWith("output-speaker-") || (canVerifiedDsp && tag.StartsWith("output-bridge-")) || tag.StartsWith("output-limiters-") || tag.StartsWith("output-fir-") || tag is "snapshot-save-pc" or "inspect-protocol" || (canMute && tag.StartsWith("mute-")) || (canVerifiedDsp && (tag.StartsWith("output-trim-") || tag.StartsWith("output-polarity-")));
                if (!allowed) button.IsEnabled = false;
            }
        }
    }
}
