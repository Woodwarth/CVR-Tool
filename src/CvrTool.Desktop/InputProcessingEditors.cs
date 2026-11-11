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
    private readonly Dictionary<(string Device, int Channel, string Kind), Window> inputEditors = new();
    private bool ActivateInputEditor(string device, int channel, string kind)
    {
        if (!inputEditors.TryGetValue((device, channel, kind), out var editor))
            return false;
        editor.Activate();
        return true;
    }
    private void ShowInputEditor(Window editor, string device, int channel, string kind)
    {
        var key = (device, channel, kind);
        inputEditors.Add(key, editor);
        editor.Closed += (_, _) => inputEditors.Remove(key);
        editor.Show(this);
    }
    private Button DelayControl(int channel)
    {
        var input = service.GetInputProcessing(deviceId, channel);
        var label = new StackPanel { Spacing = 4 };
        label.Children.Add(Centered("Delay", 13));
        label.Children.Add(Centered(AmplifierService.DelayMeters(input.DelayMs).ToString("0.00") + " m", 12));
        label.Children.Add(Centered(input.DelayMs.ToString("0.00") + " ms", 12, Muted));
        var button = new Button { Content = label, Background = Avalonia.Media.Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), Tag = "input-delay-" + channel, Height = 72, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) };
        button.Click += (_, _) => OpenDelayEditor(channel);
        return button;
    }
    private Button InputEqControl(int channel)
    {
        var input = service.GetInputProcessing(deviceId, channel);
        var label = new StackPanel { Spacing = 6 };
        label.Children.Add(Centered("EQ", 13));
        label.Children.Add(Centered(input.EqEnabled ? "On" : "Off", 13, input.EqEnabled ? Accent : Muted));
        var button = new Button { Content = label, Background = Avalonia.Media.Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), Tag = "input-eq-" + channel, Height = 72, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(4) };
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
    private void OpenDelayEditor(int channel,bool output=false)
    {
        var editedId = deviceId;
        var kind=output ? "output-delay" : "delay";var side=output ? "Output" : "Input";
        if (ActivateInputEditor(editedId, channel, kind))
            return;
        var input = output ? service.GetOutputProcessing(editedId,channel) : service.GetInputProcessing(editedId, channel);
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        content.Children.Add(Heading(side+" delay · " + channel));
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
        var editor = EditorWindow(side+" delay · Channel " + channel, content, 400, 340);
        apply.Click += (_, _) =>
        {
            if (ms.Value is null)
                return;
            try
            {
                var change=new InputProcessingChange(editedId,channel,DelayMs:(double)ms.Value.Value);
                if(output)service.ApplyOutputProcessing(service.PlanOutputProcessing(change).Id);else service.ApplyInputProcessing(service.PlanInputProcessing(change).Id);
                Render();
                editor.Close();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, kind);
    }
}
