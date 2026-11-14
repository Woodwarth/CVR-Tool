// Desktop / UI helpers — shared typography, spacing, cards and numeric editors.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private static Control Stage(string label, string caption, bool available)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };
        stack.Children.Add(Text(label, 14, available ? Accent : Ink));
        stack.Children.Add(Text(caption, 10, Muted));
        return new Border
        {
            Child = stack,
            Padding = new Thickness(12),
            BorderBrush = available ? Accent : Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            MinWidth = 90,
            MinHeight = 68
        };
    }

    private static Control Field(string label, Control editor)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };
        stack.Children.Add(Text(label, 12, Muted));
        stack.Children.Add(editor);
        return stack;
    }

    private static Control Labeled(string label, Control value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*")
        };
        grid.Children.Add(Text(label, 12, Muted));
        AddColumn(grid, value, 1);
        return grid;
    }

    private static void AddColumn(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private ColumnDefinitions EqualColumns() => new(string.Join(",", Enumerable.Repeat("*", channels.Count)));
    private static NumericUpDown Number(decimal min, decimal max, decimal increment, decimal value) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = increment,
        Value = value,
        FormatString = "0.0",
        ShowButtonSpinner = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinWidth = 45,
        Padding = new Thickness(5, 6)
    };
    private static TextBlock Heading(string text) => Text(text, 19, weight: FontWeight.SemiBold);
    private static TextBlock Centered(string text, double size, IBrush? brush = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Ink,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private static TextBlock Text(string text, double size, IBrush? brush = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Ink,
        FontWeight = weight ?? FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    private static Border Card(Control child) => new()
    {
        Child = child,
        Padding = new Thickness(18),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(15),
        Background = Appearance.Glass
    };
}
