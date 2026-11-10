// Desktop / Input processing editors
// Purpose: edit input delay, EQ enable state and eight EQ bands.
// Units: delay ms/metres at 343 m/s; EQ Hz, dB and Q.
// State: changes are applied only through the shared Application service.
// Scope: identical controls for every channel on two-/four-channel devices.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Button DelayControl(int channel)
    {
        var input = service.GetInputProcessing(deviceId, channel);
        var label = new StackPanel { Spacing = 4 };
        label.Children.Add(Centered("Delay", 13));
        label.Children.Add(Centered(AmplifierService.DelayMeters(input.DelayMs).ToString("0.00") + " m", 12));
        label.Children.Add(Centered(input.DelayMs.ToString("0.00") + " ms", 12, Muted));
        var button = new Button { Content = label, Background = Avalonia.Media.Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), Tag = "input-delay-" + channel, Height = 80, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) };
        button.Click += (_, _) => OpenDelayEditor(channel);
        return button;
    }
    private Button InputEqControl(int channel)
    {
        var input = service.GetInputProcessing(deviceId, channel);
        var label = new StackPanel { Spacing = 6 };
        label.Children.Add(Centered("EQ", 13));
        label.Children.Add(Centered(input.EqEnabled ? "On" : "Off", 13, input.EqEnabled ? Accent : Muted));
        var button = new Button { Content = label, Background = Avalonia.Media.Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), Tag = "input-eq-" + channel, Height = 80, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) };
        button.Click += (_, _) => OpenInputEqEditor(channel);
        return button;
    }
    private Window EditorWindow(string title, Control content, double width, double height) => new()
    {
        Title = title,
        Content = content,
        Width = width,
        Height = height,
        MinWidth = Math.Min(width, 420),
        MinHeight = Math.Min(height, 280),
        Background = Background,
        Foreground = Foreground,
        Icon = Branding.WindowIcon(),
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };
    private void OpenDelayEditor(int channel)
    {
        var editedId = deviceId;
        var input = service.GetInputProcessing(editedId, channel);
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        content.Children.Add(Heading("Input delay · " + channel));
        var ms = Number(0, 100, .01m, (decimal)input.DelayMs);
        ms.Tag = "delay-ms";
        var meters = Number(0, (decimal)AmplifierService.DelayMeters(100), .01m, (decimal)AmplifierService.DelayMeters(input.DelayMs));
        meters.Tag = "delay-meters";
        ms.FormatString = meters.FormatString = "0.000";
        var syncing = false;
        ms.ValueChanged += (_, _) => { if (syncing || ms.Value is null) return; syncing = true; meters.Value = (decimal)AmplifierService.DelayMeters((double)ms.Value.Value); syncing = false; };
        meters.ValueChanged += (_, _) => { if (syncing || meters.Value is null) return; syncing = true; ms.Value = (decimal)AmplifierService.DelayMilliseconds((double)meters.Value.Value); syncing = false; };
        content.Children.Add(Field("Time · ms", ms));
        content.Children.Add(Field("Distance · m", meters));
        content.Children.Add(Text("Distance assumes 343 m/s. Delay is stored in milliseconds.", 11, Muted));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(actions);
        var apply = new Button { Content = "Apply", Tag = "apply-delay" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        var editor = EditorWindow("Input delay · Channel " + channel, content, 400, 340);
        apply.Click += (_, _) =>
        {
            if (ms.Value is null)
                return;
            try
            {
                service.ApplyInputProcessing(service.PlanInputProcessing(new(editedId, channel, DelayMs: (double)ms.Value.Value)).Id);
                Render();
                editor.Close();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        editor.Show(this);
    }
    private void OpenInputEqEditor(int channel)
    {
        var editedId = deviceId;
        var input = service.GetInputProcessing(editedId, channel);
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        content.Children.Add(Heading("Input EQ · Channel " + channel));
        var enabled = new CheckBox { Content = "EQ On", IsChecked = input.EqEnabled, Tag = "input-eq-enabled" };
        content.Children.Add(enabled);
        enabled.IsCheckedChanged += (_, _) => enabled.Content = enabled.IsChecked == true ? "EQ On" : "EQ Off";
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,*,*,70"), ColumnSpacing = 10 };
        foreach (var (label, column) in new[] { ("Band", 0), ("Frequency · Hz", 1), ("Gain · dB", 2), ("Q", 3), ("Enabled", 4) })
            AddColumn(heading, Text(label, 12, Muted), column);
        content.Children.Add(heading);
        var rows = new List<(EqualizerBand Band, NumericUpDown Frequency, NumericUpDown Gain, NumericUpDown Q, CheckBox Enabled)>();
        foreach (var band in input.Bands)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,*,*,70"), ColumnSpacing = 10 };
            row.Children.Add(Text("EQ " + band.Number, 13));
            var frequency = Number(20, 20000, 1, (decimal)band.FrequencyHz);
            frequency.FormatString = "0";
            frequency.Tag = "eq-frequency-" + band.Number;
            var gain = Number(-18, 18, .1m, (decimal)band.GainDb);
            gain.Tag = "eq-gain-" + band.Number;
            var q = Number(.1m, 20, .01m, (decimal)band.Q);
            q.FormatString = "0.00";
            q.Tag = "eq-q-" + band.Number;
            var active = new CheckBox { IsChecked = band.Enabled, Tag = "eq-band-enabled-" + band.Number };
            AddColumn(row, frequency, 1);
            AddColumn(row, gain, 2);
            AddColumn(row, q, 3);
            AddColumn(row, active, 4);
            content.Children.Add(row);
            rows.Add((band, frequency, gain, q, active));
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-input-eq" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        content.Children.Add(actions);
        var message = Text("Eight EQ bands · Input EQ settings", 12, Muted);
        content.Children.Add(message);
        var editor = EditorWindow("Input EQ · Channel " + channel, new ScrollViewer { Content = content }, 720, 570);
        apply.Click += (_, _) =>
        {
            if (rows.Any(r => r.Frequency.Value is null || r.Gain.Value is null || r.Q.Value is null))
            {
                message.Text = "Enter all EQ values.";
                return;
            }
            var bands = rows.Select(r => r.Band with { FrequencyHz = (double)r.Frequency.Value!.Value, GainDb = (double)r.Gain.Value!.Value, Q = (double)r.Q.Value!.Value, Enabled = r.Enabled.IsChecked == true }).ToArray();
            try
            {
                service.ApplyInputProcessing(service.PlanInputProcessing(new(editedId, channel, EqEnabled: enabled.IsChecked == true, Bands: bands)).Id);
                Render();
                editor.Close();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        editor.Show(this);
    }
}
