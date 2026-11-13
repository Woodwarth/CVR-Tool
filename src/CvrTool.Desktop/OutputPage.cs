// Desktop / Output signal flow
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present per-channel FIR, EQ, trim, delay, polarity and mute like the reference.
// Units: dB and ms; processing editors use the shared application service.
// State: each row belongs to one output; confirmed edits reload current state.
// Scope: load mode/gate/limiters are per output; stereo/bridge applies to a pair.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private const double OutputBlockWidth = 108, OutputBlockHeight = 76;

    // Every tile shares fixed header/value rows, including gain and symbol mute.
    private static Grid OutputBlockContent(string title, Control value)
    {
        var content = new Grid { RowDefinitions = new RowDefinitions("20,*"), Height = 58, Width = 94 };
        var heading = Centered(title, 13);
        heading.FontWeight = FontWeight.Normal;
        heading.Tag = "output-block-title";
        content.Children.Add(heading);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(value, 1);
        content.Children.Add(value);
        return content;
    }
    private Control OutputsPage()
    {
        var panel = new StackPanel { Spacing = 16, Tag = "outputs-page" };
        var metadata = service.GetNativePresetMetadata(deviceId);
        panel.Children.Add(Heading("Outputs · " + deviceName.Text));
        foreach (var output in service.GetDevice(deviceId).Outputs)
        {
            var eq = service.GetOutputProcessing(deviceId, output.Number);
            var fir = service.GetFir(deviceId, output.Number);
            var dynamics = service.GetOutputDynamics(deviceId, output.Number);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("108", 12))), ColumnSpacing = 8, Height = 100, Tag = "output-flow-" + output.Name };
            Button Block(string title, string value, string tag, Action action)
            {
                var caption = Centered(value, 12, Accent); caption.MaxWidth = 94; caption.TextTrimming = TextTrimming.CharacterEllipsis;
                var button = new Button { Content = OutputBlockContent(title, caption), Tag = tag, Width = OutputBlockWidth, Height = OutputBlockHeight, Padding = new Thickness(6, 8), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
                button.Click += (_, _) => action();
                return button;
            }
            AddColumn(row, Block("Source", "Routing", "output-source-" + output.Name, () => { activeTab = "Inputs"; Render(); }), 0);
            AddColumn(row, Block("FIR", fir.Enabled ? "On" : "Off", "output-fir-" + output.Name, () => OpenFirEditor(output.Number)), 1);
            AddColumn(row, Block("EQ", eq.EqEnabled ? "On" : "Off", "output-eq-" + output.Name, () => OpenInputEqEditor(output.Number, true)), 2);
            var gain = new GainReadout { FontSize = 12, FontWeight = FontWeight.Normal, Foreground = Accent, Width = 94, Tag = "output-gain-" + output.Name, Value = output.GainDb };
            gain.ValueChanged += value => Commit(new(deviceId, output.Number, GainDb: value));
            var trim = Block("Trim", "", "output-trim-" + output.Name, gain.BeginEdit);
            trim.Content = OutputBlockContent("Trim", gain);
            AddColumn(row, trim, 3);
            AddColumn(row, Block("Delay", $"{AmplifierService.DelayMeters(output.DelayMs):0.00} m\n{output.DelayMs:0.00} ms", "output-delay-" + output.Name, () => OpenDelayEditor(output.Number, true)), 4);
            AddColumn(row, Block("Polarity", output.Inverted ? "180°" : "0°", "output-polarity-" + output.Name, () => { Commit(new(deviceId, output.Number, Inverted: !output.Inverted)); Render(); }), 5);
            AddColumn(row, Block("Limiters", "Peak / RMS", "output-limiters-" + output.Name, () => OpenLimiterEditor(output.Number)), 6);
            AddColumn(row, Block("Mode", OutputLoadModes.Label(dynamics.LoadMode), "output-mode-" + output.Name, () => OpenModeEditor(output.Number)), 7);
            AddColumn(row, Block("Noise Gate", (dynamics.NoiseGateEnabled ? "On" : "Off") + "\n" + (dynamics.NoiseGateDb is { } db ? db + " dB" : "—"), "output-noise-" + output.Name, () => OpenNoiseGateEditor(output.Number)), 8);
            var mute = MuteSwitch(output.Muted, "Output " + output.Name, value => { Commit(new(deviceId, output.Number, Muted: value)); Render(); }, width: OutputBlockWidth, height: OutputBlockHeight, glyphSize: 24, heading: "Mute");
            mute.Tag = "mute-editor-" + output.Name;
            mute.Padding = new Thickness(6, 8);
            mute.CornerRadius = new CornerRadius(3);
            mute.VerticalAlignment = VerticalAlignment.Center;
            AddColumn(row, mute, 9);
            AddColumn(row, Block("Output " + output.Name, metadata?.Channels.Single(c => c.Channel == output.Number).SpeakerName is { Length: > 0 } speaker ? speaker : "Speaker", "output-speaker-" + output.Name, () => { activeTab = "Main"; Render(); }), 10);
            var pairStart = (output.Number - 1) / 2 * 2 + 1;
            var bridge = Block("", "", "output-bridge-" + output.Name, () => ToggleBridge(output.Number));
            bridge.Content = Centered(dynamics.Bridged ? "Bridge" : "Stereo", 13, Accent);
            AddColumn(row, bridge, 11);
            var followsBridge = dynamics.Bridged && output.Number % 2 == 0;
            if (followsBridge)
            {
                foreach (var control in row.Children.Where(c => Grid.GetColumn(c) < 11))
                {
                    control.IsEnabled = false;
                    control.Opacity = .35;
                    ToolTip.SetTip(control, $"Output {output.Name} follows {(char)('A' + pairStart - 1)} in bridge mode.");
                }
                CloseBridgeFollowerEditors(output.Number);
            }
            var channelCard = Card(row);
            channelCard.Padding = new Thickness(12);
            panel.Children.Add(channelCard);
        }
        return panel;
    }
}
