// Desktop / Fixed design surface
// Purpose: preserve arrangement while scaling the whole interface uniformly.
// Input: window size; a minimum size limits unreadably small controls.
// State: the design canvas stays 1680 by 980 logical pixels.
// Scope: page scroll areas remain usable inside the scaled canvas.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control BuildShell()
    {
        var shell = new Grid { Width = 1680, Height = 980, RowDefinitions = new RowDefinitions("64,*,34"), Background = Background, Tag = "design-surface" };
        shell.Children.Add(Header());
        var center = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*") };
        Grid.SetRow(center, 1);
        shell.Children.Add(center);
        var sidebar = new StackPanel { Spacing = 12, Margin = new Thickness(12) };
        sidebar.Children.Add(Heading("Devices"));
        sidebar.Children.Add(navigation);
        sidebar.Children.Add(Text("MCP shares this session\nManual targets in Network", 11, Muted));
        center.Children.Add(new Border { Background = Brush.Parse("#192129"), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });
        var workspace = new Grid { RowDefinitions = new RowDefinitions("84,48,*"), RowSpacing = 12, Margin = new Thickness(16), Tag = "workspace" };
        workspace.Children.Add(DeviceHeader());
        Grid.SetRow(tabs, 1);
        workspace.Children.Add(tabs);
        var scroll = new ScrollViewer { Content = page, Tag = "workspace-scroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        workspace.Children.Add(scroll);
        Grid.SetColumn(workspace, 1);
        center.Children.Add(workspace);
        var footer = new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 8), Child = status };
        Grid.SetRow(footer, 2);
        shell.Children.Add(footer);
        nameEditor.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                SaveDeviceName();
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                ShowDeviceName();
                e.Handled = true;
            }
        };
        return new Viewbox { Child = shell, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Tag = "scaled-shell" };
    }
    private void FitInitialWindow()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width / scale - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height / scale - 24));
        Position = new PixelPoint(area.X + (int)((area.Width - Width * scale) / 2), area.Y + (int)((area.Height - Height * scale) / 2));
    }
    private Control Header()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        row.Children.Add(new Image { Source = Branding.Bitmap(), Width = 38, Height = 38 });
        row.Children.Add(Text("CVR Tool", 27, weight: FontWeight.Bold));
        row.Children.Add(new Border { Child = Text("LOCAL SESSION", 12, Accent, FontWeight.SemiBold), Padding = new Thickness(12, 6), BorderBrush = Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center });
        return new Border { Child = row, Padding = new Thickness(16, 10), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
}
