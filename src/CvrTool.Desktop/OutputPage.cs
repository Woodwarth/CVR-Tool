// Desktop / Output signal flow
// Purpose: present per-channel FIR, EQ, trim, delay, polarity and mute like the reference.
// Units: dB and ms; processing editors use the shared application service.
// State: each row belongs to one output; confirmed edits reload current state.
// Scope: unimplemented limiter/mode/noise controls remain visibly unavailable.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control OutputsPage()
    {
        var panel = new StackPanel { Spacing = 24 };
        panel.Children.Add(Heading("Outputs · " + deviceName.Text));
        foreach (var output in service.GetDevice(deviceId).Outputs)
        {
            var eq = service.GetOutputProcessing(deviceId, output.Number);
            var fir = service.GetFir(deviceId, output.Number);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,100,100,115,115,100,110,100,110,70,100"), ColumnSpacing = 8, Height = 100, Tag = "output-flow-" + output.Name };
            Button Block(string title, string value, string tag, Action? action)
            {
                var text = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(Centered(title, 13));
                text.Children.Add(Centered(value, 12, action is null ? Muted : Accent));
                var button = new Button { Content = text, Tag = tag, Height = 76, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Background = Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), IsEnabled = action is not null };
                if (action is not null)
                    button.Click += (_, _) => action();
                return button;
            }
            AddColumn(row, Block("Source", "Routing", "output-source-" + output.Name, () => { activeTab = "Inputs"; Render(); }), 0);
            AddColumn(row, Block("FIR", fir.Enabled ? "On" : "Off", "output-fir-" + output.Name, () => OpenFirEditor(output.Number)), 1);
            AddColumn(row, Block("EQ", eq.EqEnabled ? "On" : "Off", "output-eq-" + output.Name, () => OpenInputEqEditor(output.Number, true)), 2);
            var gain = new GainReadout { Value = output.GainDb, Tag = "output-gain-" + output.Name, Width = 105 };
            gain.ValueChanged += value => Commit(new(deviceId, output.Number, GainDb: value));
            var trim = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            trim.Children.Add(Centered("Trim", 13));
            trim.Children.Add(gain);
            AddColumn(row, trim, 3);
            AddColumn(row, Block("Delay", $"{AmplifierService.DelayMeters(output.DelayMs):0.00} m\n{output.DelayMs:0.00} ms", "output-delay-" + output.Name, () => OpenDelayEditor(output.Number, true)), 4);
            AddColumn(row, Block("Polarity", output.Inverted ? "180°" : "0°", "output-polarity-" + output.Name, () => { Commit(new(deviceId, output.Number, Inverted: !output.Inverted)); Render(); }), 5);
            AddColumn(row, Block("Limiters", "Unavailable", "output-limiters-" + output.Name, null), 6);
            AddColumn(row, Block("Mode", "Unavailable", "output-mode-" + output.Name, null), 7);
            AddColumn(row, Block("Noise Gate", "Unavailable", "output-noise-" + output.Name, null), 8);
            var mute = MuteSwitch(output.Muted, "Output " + output.Name, value => Commit(new(deviceId, output.Number, Muted: value)));
            mute.Tag = "mute-editor-" + output.Name;
            mute.HorizontalAlignment = HorizontalAlignment.Center;
            mute.VerticalAlignment = VerticalAlignment.Center;
            AddColumn(row, mute, 9);
            AddColumn(row, Block("Output " + output.Name, "Speaker", "output-speaker-" + output.Name, () => { activeTab = "Main"; Render(); }), 10);
            panel.Children.Add(Card(row));
        }
        return panel;
    }
}
