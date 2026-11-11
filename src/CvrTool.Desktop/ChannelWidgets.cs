// Desktop / Channel widgets — channel frames, LED rows and symbol-only mute controls.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private static Border ChannelGroup(Control content, string tag) => new()
    {
        Child = content,
        Width = ChannelWidth,
        Margin = new Thickness(6),
        Tag = tag,
        Padding = new Thickness(8),
        CornerRadius = new CornerRadius(6),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Background = Brush.Parse("#182129")
    };
    private static Control InputsPanel() => Text("Input data and topology are not confirmed for this demo.", 14, Muted);
    private static Control StatusLight(string? label, bool? value)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var onColor = Color.Parse("#FF566A");
        row.Children.Add(new StatusLed(value, onColor));
        if (label is not null)
            row.Children.Add(Text(label, 11, value == true ? new SolidColorBrush(onColor) : Muted));
        ToolTip.SetTip(row, (label ?? "Status") + ": " + (value is null ? "Unknown / not measured" : value.Value ? "Active" : "Inactive"));
        return row;
    }

    private static ToggleButton MuteSwitch(bool initial, string name, Action<bool> changed, double width = 32, double height = 28, double glyphSize = 18)
    {
        var button = new ToggleButton
        {
            IsChecked = initial,
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(5),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5)
        };
        // Keep the checked state red instead of Fluent's default grey selection fill.
        button.Template = new FuncControlTemplate<ToggleButton>((control, _) => new Border { [!Border.BackgroundProperty] = control[!BackgroundProperty], [!Border.BorderBrushProperty] = control[!BorderBrushProperty], [!Border.CornerRadiusProperty] = control[!CornerRadiusProperty], [!Border.BorderThicknessProperty] = control[!BorderThicknessProperty], Padding = new Thickness(5), Child = new ContentPresenter { [!ContentPresenter.ContentProperty] = control[!ContentProperty], HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center } });
        void Paint()
        {
            var isMuted = button.IsChecked == true;
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(glyphSize == 18 ? new SpeakerGlyph(isMuted) { Width = 18, Height = 18 } : new Viewbox { Width = glyphSize, Height = glyphSize, Child = new SpeakerGlyph(isMuted) { Width = 20, Height = 18 } });
            button.Content = content;
            button.Background = Brush.Parse(isMuted ? "#BE3044" : "#182129");
            button.BorderBrush = isMuted ? Brush.Parse("#ED5D6F") : Line;
            AutomationProperties.SetName(button, name + (isMuted ? " muted" : " unmuted"));
            ToolTip.SetTip(button, name + (isMuted ? ": muted — click to unmute" : ": unmuted — click to mute"));
        }

        button.GotFocus += (_, _) => button.BorderThickness = new Thickness(3);
        button.LostFocus += (_, _) => button.BorderThickness = new Thickness(1);
        Paint();
        button.IsCheckedChanged += (_, _) =>
        {
            Paint();
            changed(button.IsChecked == true);
        };
        return button;
    }
}
