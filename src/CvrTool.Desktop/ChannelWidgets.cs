// Desktop / Channel widgets — channel frames, LED rows and symbol-only mute controls.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
        CornerRadius = new CornerRadius(12),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Background = Appearance.Strip
    };
    private static Grid MeterScale(int floor)
    {
        var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*,*,*,*"),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1, 2, 2) };
        for (var i = 0; i <= 6; i++)
        {
            var label = Text(Math.Round(floor * i / 6.0).ToString("0"), 10, Muted);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = i == 6 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            Grid.SetRow(label, Math.Min(i, 5));
            scale.Children.Add(label);
        }
        return scale;
    }

    private static Control InputsPanel() => Text("Input data and topology are not confirmed for this demo.", 14, Muted);
    private static readonly SolidColorBrush ClipColor = new(Color.Parse("#FF566A"));
    private static Control StatusLight(string? label, bool? value)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var onColor = label switch
        {
            "Limit" or "Load" or "Open" => Color.Parse("#E9A538"),
            "Stby" => Color.Parse("#729EEC"),
            "Bridge" or "Hi-Z" => Color.Parse("#16BEAA"),
            _ => ClipColor.Color
        };
        row.Children.Add(new StatusLed(value, onColor));
        if (label is not null)
            row.Children.Add(Text(label, 11, value == true ? new SolidColorBrush(onColor) : Muted));
        ToolTip.SetTip(row, (label ?? "Status") + ": " + (value is null ? "Unknown / not measured" : value.Value ? "Active" : "Inactive"));
        return row;
    }

    private static ToggleButton MuteSwitch(bool initial, string name, Action<bool> changed, double width = 32, double height = 28, double glyphSize = 18, string? heading = null)
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
        button.Template = new FuncControlTemplate<ToggleButton>((control, _) => new Border { [!Border.BackgroundProperty] = control[!BackgroundProperty], [!Border.BorderBrushProperty] = control[!BorderBrushProperty], [!Border.CornerRadiusProperty] = control[!CornerRadiusProperty], [!Border.BorderThicknessProperty] = control[!BorderThicknessProperty], Padding = control.Padding, Child = new ContentPresenter { [!ContentPresenter.ContentProperty] = control[!ContentProperty], HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center } });
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
            button.Content = heading is null ? content : OutputBlockContent(heading, content);
            button.Background = isMuted ? Brush.Parse("#BE3044") : Appearance.Surface;
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
