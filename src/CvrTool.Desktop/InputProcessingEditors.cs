// Desktop / Input processing editors
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
        if (service.GetDevice(device).Readings?.ReadOnly == true)
        {
            ConfigureLiveEditor(editor, device, channel, kind);
        }
        editor.Show(this);
    }
    private Button DelayControl(int channel)
    {
        var input = service.GetInputProcessing(deviceId, channel);
        var delay = Centered($"{AmplifierService.DelayMeters(input.DelayMs):0.00} m\n{input.DelayMs:0.00} ms", 12, Accent);
        var button = new Button { Content = OutputBlockContent("Delay", delay), Background = Avalonia.Media.Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), Tag = "input-delay-" + channel, Width = OutputBlockWidth, Height = OutputBlockHeight, HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(6, 8), CornerRadius = new CornerRadius(3) };
        button.Click += (_, _) => OpenDelayEditor(channel);
        return button;
    }
    private Button InputEqControl(int channel)
    {
        var input = service.GetInputProcessing(deviceId, channel);
        var enabled = Centered(input.EqEnabled ? "On" : "Off", 12, input.EqEnabled ? Accent : Muted);
        var button = new Button { Content = OutputBlockContent("EQ", enabled), Background = Avalonia.Media.Brush.Parse("#182129"), BorderBrush = Line, BorderThickness = new Thickness(1), Tag = "input-eq-" + channel, Width = OutputBlockWidth, Height = OutputBlockHeight, HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(6, 8), CornerRadius = new CornerRadius(3) };
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
        var ms = new TextBox { Text = input.DelayMs.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), Tag = "delay-ms" };
        var meters = new TextBox { Text = AmplifierService.DelayMeters(input.DelayMs).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), Tag = "delay-meters" };
        var validation = Text("", 12, Muted);
        var apply = new Button { Content = "Apply", Tag = "apply-delay" };
        double? pendingMs = input.DelayMs;
        var syncing = false;
        void Synchronize(TextBox source, TextBox other, bool distance)
        {
            if (syncing) return;
            var valid = double.TryParse(source.Text?.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0;
            var milliseconds = distance ? AmplifierService.DelayMilliseconds(value) : value;
            valid = valid && milliseconds <= 100;
            pendingMs = valid ? milliseconds : null;
            apply.IsEnabled = valid;
            validation.Text = valid ? "" : "Enter 0–100 ms or 0–34.3 m.";
            if (!valid) return;
            syncing = true;
            try { other.Text = (distance ? milliseconds : AmplifierService.DelayMeters(milliseconds)).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture); }
            finally { syncing = false; }
        }
        ms.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Synchronize(ms, meters, false); };
        meters.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Synchronize(meters, ms, true); };
        content.Children.Add(Field("Time · ms", ms));
        content.Children.Add(Field("Distance · m", meters));
        content.Children.Add(Text("Distance assumes 343 m/s. Delay is stored in milliseconds.", 11, Muted));
        content.Children.Add(validation);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(actions);
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        var editor = EditorWindow(side+" delay · Channel " + channel, content, 400, 340);
        apply.Click += async (_, _) =>
        {
            if (pendingMs is null) return;
            try
            {
                var change=new InputProcessingChange(editedId,channel,DelayMs:pendingMs.Value);
                void Apply() { if(output) service.Apply(service.Plan(new(editedId,channel,DelayMs:pendingMs.Value)).Id); else service.ApplyInputProcessing(service.PlanInputProcessing(change).Id); }
                if (service.GetDevice(editedId).Readings?.ReadOnly == true) await Task.Run(Apply); else Apply();
                Render();
                editor.Close();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, kind);
    }
}
