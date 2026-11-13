// Desktop / Live observation details
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: display every decoded setting and clearly identify unavailable readings.
// Units: DSP-native gain, ms/metres, Hz, Q, volts, amperes and degrees Celsius.
// State: this view uses the same shared cached snapshot as UI and MCP clients.
// Scope: opaque bytes, uncalibrated meters and unread FIR coefficients are not invented.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private static string OnOff(bool value) => value ? "On" : "Off";
    private static string Known(double? value, string unit = "") => value is { } number ? number.ToString("0.###") + unit : "—";
    private static string Flag(bool? value) => value is null ? "Unknown" : value.Value ? "On" : "Off";
    private Control LiveDetailsPage()
    {
        var snapshot = service.GetDevice(deviceId);
        var data = snapshot.Readings!;
        var root = new StackPanel { Spacing = 16, Tag = "live-details" };
        void Values(string title, params (string Label, string Value)[] values)
        {
            var box = new StackPanel { Spacing = 8 };
            box.Children.Add(Heading(title));
            foreach (var (label, value) in values)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), ColumnSpacing = 16 };
                row.Children.Add(Text(label, 13, Muted)); AddColumn(row, Text(value, 13), 1); box.Children.Add(row);
            }
            root.Children.Add(Card(box));
        }
        var inspectedDevice = deviceId;
        var inspect = new Button { Content = "Inspect protocol reply", Tag = "inspect-protocol" };
        inspect.Click += async (_, _) =>
        {
            var options = new (int Id, string Label)[] { (43,"FIR"), (50,"Stereo / bridge"), (52,"Output EQ"),
                (54,"Peak limiter"), (55,"RMS limiter"), (59,"Preset names"), (62,"Source alignment"),
                (82,"RTA"), (88,"Input delay switch"), (93,"Input group gain") };
            var choice = new ComboBox { ItemsSource = options.Select(o => $"{o.Id} · {o.Label}").ToArray(), SelectedIndex = 0 };
            var channel = new ComboBox { ItemsSource = snapshot.Outputs.Select(o => o.Number).ToArray(), SelectedIndex = 0 };
            var source = new ComboBox { ItemsSource = new[] { "Analog", "Digital", "AES" }, SelectedIndex = 0, IsEnabled = false };
            choice.SelectionChanged += (_, _) => source.IsEnabled = choice.SelectedIndex >= 0 && options[choice.SelectedIndex].Id == 62;
            var result = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var read = new Button { Content = "Read from device" };
            read.Click += async (_, _) =>
            {
                int id = options[choice.SelectedIndex].Id, ch = channel.SelectedIndex + 1, seg = source.IsEnabled ? source.SelectedIndex : 0;
                read.IsEnabled = false;
                try { result.Text = await Task.Run(() => service.InspectProtocol(inspectedDevice, id, ch, seg)); }
                catch (Exception ex) { result.Text = ex.Message; }
                finally { read.IsEnabled = true; }
            };
            var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(18) };
            panel.Children.Add(Field("Function", choice)); panel.Children.Add(Field("Channel", channel));
            panel.Children.Add(Field("Source", source)); panel.Children.Add(read); panel.Children.Add(result);
            await EditorWindow("Protocol inspection", panel, 620, 460).ShowDialog(this);
        };
        root.Children.Add(inspect);
        var controls = service.GetControlState(deviceId);
        Values("Live device · Limited editing", ("Name", snapshot.Name), ("Firmware", data.FirmwareHeader),
            ("MAC address", data.MacAddress ?? "—"), ("Operating time", DeviceMetadataCodec.FormatOperatingTime(data.OperatingMinutes)), ("Preset", data.CurrentPreset ?? "—"),
            ("Settings received", data.SettingsObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            ("Status received", data.TelemetryObservedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—"),
            ("Connection", data.Error ?? "Connected"), ("Display lock", OnOff(controls.DisplayLocked)),
            ("Wink", Flag(controls.Wink)), ("Standby", controls.StandbyKnown ? OnOff(controls.Standby) : "Unknown"), ("Power · reported", Known(data.Power)),
            ("Main meters", "Input signal and output voltage relative to channel RMS maximum · −40 to 0 dB"));
        void Processing(string title, InputProcessingState state)
        {
            string Crossover(CrossoverFilter? filter) => filter is null ? "—" :
                $"{OnOff(filter.Enabled)} · {CrossoverFilters.Options.FirstOrDefault(o => o.Code == filter.FilterType)?.ShortName ?? "Type " + filter.FilterType} · {filter.FrequencyHz:0.###} Hz";
            Values(title, ("Gain", Known(state.GainDb, " dB")), ("Delay", $"{state.DelayMs:0.###} ms · {AmplifierService.DelayMeters(state.DelayMs):0.###} m"),
                ("EQ", OnOff(state.EqEnabled)), ("HP", Crossover(state.HighPass)), ("LP", Crossover(state.LowPass)));
            var table = new StackPanel { Spacing = 8 };
            Grid Row(string[] cells)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,100,150,150,100,140"), ColumnSpacing = 12 };
                for (var i = 0; i < cells.Length; i++) AddColumn(row, Text(cells[i], 13), i);
                return row;
            }
            table.Children.Add(Row(["Band", "State", "Frequency", "Gain", "Q", "Type"]));
            foreach (var band in state.Bands)
                table.Children.Add(Row([band.Number.ToString(), OnOff(band.Enabled), Known(band.FrequencyHz, " Hz"), Known(band.GainDb, " dB"), Known(band.Q), band.FilterType == 0 ? "Peak" : "Type " + band.FilterType]));
            root.Children.Add(Card(table));
        }
        for (var ch = 1; ch <= snapshot.Capabilities.InputCount; ch++) Processing($"Input {ch}", service.GetInputProcessing(deviceId, ch));
        var routes = new StackPanel { Spacing = 8 }; routes.Children.Add(Heading("Routing matrix"));
        foreach (var route in service.GetRouting(deviceId).Cells.OrderBy(c => c.Output).ThenBy(c => c.Input))
            routes.Children.Add(Text($"Input {route.Input} → Output {(char)('A' + route.Output - 1)}    {route.GainDb:0.###} dB    {(route.Muted ? "Muted" : "Enabled")}", 13));
        root.Children.Add(Card(routes));
        var telemetry = service.GetTelemetry(deviceId);
        foreach (var output in snapshot.Outputs)
        {
            var ch = output.Number;
            var dynamics = service.GetOutputDynamics(deviceId, ch);
            var fir = service.GetFir(deviceId, ch);
            var reading = telemetry.Single(t => t.Channel == ch);
            Values("Output " + output.Name,
                ("Trim / mute", $"{output.GainDb:0.###} dB · {(output.Muted ? "Muted" : "Unmuted")}"),
                ("Polarity", output.Inverted ? "180°" : "0°"), ("Mode", OutputLoadModes.Label(dynamics.LoadMode)),
                ("Stereo / bridge", dynamics.Bridged ? "Bridge" : "Stereo"),
                ("Noise gate", OnOff(dynamics.NoiseGateEnabled) + " · " + (dynamics.NoiseGateDb?.ToString() ?? "—") + " dB"),
                ("FIR", OnOff(fir.Enabled) + " · coefficients not received"),
                ("Voltage / current / load", Known(reading.Voltage, " V") + " · " + Known(reading.Current, " A") + " · " + Known(reading.LoadOhms, " Ω")),
                ("Temperature", Known(reading.TemperatureC, " °C")),
                ("Status code", data.StatusCodes?[ch - 1].ToString() ?? "—"),
                ("Fault / Load / Open", Flag(reading.Status?.Fault) + " / " + Flag(reading.Status?.Load) + " / " + Flag(reading.Status?.Open)),
                ("Temperature / Clip / Standby", Flag(reading.Status?.OverTemperature) + " / " + Flag(reading.Status?.Clip) + " / " + Flag(reading.Status?.Standby)),
                ("Hi-Z / Bridged status", Flag(reading.Status?.HighImpedance) + " / " + Flag(reading.Status?.Bridged)),
                ("RMS limiter", $"{OnOff(dynamics.Rms.Enabled)} · {dynamics.Rms.ThresholdVolts:0.###} Vrms · Maximum {dynamics.Rms.MaximumVolts:0.###} V"),
                ("RMS timing", $"Attack {dynamics.Rms.AttackMs} ms · Release ×{dynamics.Rms.ReleaseMultiplier} · Automatic {OnOff(dynamics.Rms.Automatic)}"),
                ("Peak limiter", $"{OnOff(dynamics.Peak.Enabled)} · {dynamics.Peak.ThresholdVolts:0.###} Vpeak · Maximum {dynamics.Peak.MaximumVolts:0.###} V"),
                ("Peak timing", $"Hold {dynamics.Peak.HoldMs} ms · Release {dynamics.Peak.ReleaseMs} ms"));
            Processing("Output " + output.Name + " EQ", service.GetOutputProcessing(deviceId, ch));
        }
        if (NativePresetInformation() is { } information) root.Children.Add(information);
        return root;
    }
    private Control LivePresetPage()
    {
        var root = new StackPanel { Spacing = 18 };
        root.Children.Add(Heading("Presets · Read-only"));
        root.Children.Add(Text("Current preset: " + (service.GetDevice(deviceId).Readings?.CurrentPreset ?? "—"), 16));
        var slots = service.GetDevicePresets(deviceId).Slots;
        if (slots.Count == 0)
            root.Children.Add(Text("Device preset names unavailable", 13, Muted));
        else
        {
            root.Children.Add(Heading($"Device presets · {slots.Count} slots"));
            foreach (var row in slots.Chunk(4))
                root.Children.Add(Text(string.Join("     ", row.Select(slot => $"{slot.Number:00} · {slot.Name}")), 14));
        }
        var save = new Button { Content = "Save to PC", Tag = "snapshot-save-pc" };
        save.Click += async (_, _) =>
        {
            var selected = deviceId;
            const string extension = "sd";
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save received settings", SuggestedFileName = deviceName.Text + "-settings." + extension,
                    DefaultExtension = extension, FileTypeChoices = [new("CVR native settings") { Patterns = ["*." + extension] }]
                });
                if (file is null) return;
                if (!file.Name.EndsWith(".sd", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Save the preset with the .sd extension.");
                var bytes = await Task.Run(() => service.ExportNativePreset(selected));
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await stream.WriteAsync(bytes);
                status.Text = "Received settings saved to PC";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        root.Children.Add(save);
        if (NativePresetInformation() is { } information) root.Children.Add(information);
        return root;
    }
}
