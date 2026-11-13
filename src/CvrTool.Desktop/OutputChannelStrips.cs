// Desktop / Outputs — centered meters, per-channel status and electrical readings.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control OutputMonitoring()
    {
        var panel = new StackPanel
        {
            Spacing = 12
        };
        panel.Children.Add(Heading("Outputs"));
        var strips = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        var telemetry = service.GetTelemetry(deviceId);
        for (var i = 0; i < channels.Count; i++)
        {
            var draft = channels[i];
            var data = telemetry.Single(t => t.Channel == draft.State.Number);
            var state = data.Status ?? new OutputStatus();
            var strip = new Grid
            {
                RowDefinitions = new RowDefinitions(StripRows),
                RowSpacing = 6
            };
            void Cell(Control control, int row)
            {
                Grid.SetRow(control, row);
                strip.Children.Add(control);
            }

            Cell(Centered(draft.State.Name, 17), 0);
            // Equal spacer columns keep the meter centered; status LEDs occupy the right spacer.
            var monitoring = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,24,*"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = MeterHeight
            };
            AddColumn(monitoring, new ChannelMeter(liveReadOnly ? data.OutputMeterDb : data.LevelDbFs, liveReadOnly ? -40 : -60) { Width = 24, Height = MeterHeight, Tag = "output-meter-" + draft.State.Name }, 1);
            var flags = new StackPanel
            {
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Tag = "output-flags-" + draft.State.Name
            };
            var states = new[]
            {
                ("Fault", state.Fault),
                ("Load", state.Load),
                ("Open", state.Open),
                ("Temp", state.OverTemperature),
                ("Clip", state.Clip),
                ("Stby", state.Standby),
                ("Hi-Z", state.HighImpedance),
                ("Bridge", state.Bridged),
                ("Limit", state.Limiting)
            };
            for (var flag = 0; flag < states.Length; flag++)
            {
                var light = StatusLight(states[flag].Item1, states[flag].Item2);
                light.HorizontalAlignment = HorizontalAlignment.Left;
                light.Tag = $"output-status-{draft.State.Name}-{flag}";
                if (flag == 4)
                    flags.Children.Add(new Border { Child = light, Tag = "output-clip-" + draft.State.Name });
                else
                    flags.Children.Add(light);
            }

            AddColumn(monitoring, flags, 2);
            Cell(monitoring, 1);
            var mute = MuteSwitch(draft.Muted, "Output " + draft.State.Name, value =>
            {
                draft.Muted = value;
                ApplyMute(draft);
            });
            mute.Tag = "mute-output-" + draft.State.Name;
            Cell(mute, 3);
            var readings = new[]
            {
                ("Voltage (V)", data.Voltage),
                ("Current (A)", data.Current),
                ("Load (Ω)", data.LoadOhms),
                ("Temp (°C)", data.TemperatureC)
            };
            for (var index = 0; index < readings.Length; index++)
            {
                var field = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 4
                };
                field.Children.Add(Text(readings[index].Item1, 10, Muted));
                var value = Text(readings[index].Item2?.ToString("0.0") ?? "—", 12, Muted);
                value.Tag = $"telemetry-{draft.State.Name}-{index}";
                AddColumn(field, value, 1);
                Cell(field, 4 + index);
            }

            strips.Children.Add(ChannelGroup(strip, "output-channel-" + draft.State.Name));
        }

        panel.Children.Add(strips);
        return panel;
    }
}
