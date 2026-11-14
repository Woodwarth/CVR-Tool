// Desktop / Fixed design surface
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
        var manage = new Button { Content = "+", Width = 48, Height = 44, FontSize = 29, Padding = new Thickness(0), BorderThickness = new Thickness(0), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "manage-demos" };
        Avalonia.Automation.AutomationProperties.SetName(manage, "Manage demo devices");
        manage.Click += async (_, _) =>
        {
            var models = new ComboBox { ItemsSource = CvrTool.Core.DeviceProfiles.TargetModels.Select(p => p.Model).ToArray(), SelectedItem = "DSP-1004" };
            var demos = service.ListDevices().Where(d => d.IsDemo).ToArray();
            var existing = new ComboBox { ItemsSource = demos.Select(d => d.Name).ToArray(), SelectedIndex = Array.FindIndex(demos,d => d.Id == deviceId) };
            var add = new Button { Content = "Add demo" };
            var remove = new Button { Content = "Remove demo", IsEnabled = existing.SelectedIndex >= 0 };
            existing.SelectionChanged += (_, _) => remove.IsEnabled = existing.SelectedIndex >= 0;
            var panel = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
            panel.Children.Add(Field("Model",models)); panel.Children.Add(add);
            panel.Children.Add(Field("Session demos",existing)); panel.Children.Add(remove);
            var dialog = EditorWindow("Demo devices",panel,420,340);
            add.Click += (_, _) => { if(models.SelectedItem is string model) { Select(service.AddDemo(model).Id); dialog.Close(); } };
            remove.Click += (_, _) =>
            {
                var id = demos[existing.SelectedIndex].Id;
                service.RemoveDemo(id);
                if (deviceId == id) deviceId = service.ListDevices().FirstOrDefault()?.Id ?? "";
                ReloadSession(); dialog.Close();
            };
            await dialog.ShowDialog(this);
        };
        sidebar.Children.Add(manage);
        center.Children.Add(new Border { Background = Appearance.Glass, CornerRadius = new CornerRadius(14), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });
        var workspace = new Grid { RowDefinitions = new RowDefinitions("108,48,*"), RowSpacing = 12, Margin = new Thickness(16), Tag = "workspace" };
        workspace.Children.Add(DeviceHeader());
        Grid.SetRow(tabs, 1);
        workspace.Children.Add(tabs);
        var scroll = new ScrollViewer { Content = page, Tag = "workspace-scroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        workspace.Children.Add(scroll);
        Grid.SetColumn(workspace, 1);
        center.Children.Add(workspace);
        var footer = new Border { Background = Appearance.Glass, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 8), Child = status };
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
        return new WorkspaceViewbox(shell) { Child = shell, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Tag = "scaled-shell" };
    }
    // Grow the logical workspace on wide or tall displays while keeping controls proportional.
    private sealed class WorkspaceViewbox(Grid surface) : Viewbox
    {
        protected override Size MeasureOverride(Size available)
        {
            if (double.IsFinite(available.Width) && double.IsFinite(available.Height) && available.Width > 0 && available.Height > 0)
            {
                var scale = Math.Min(available.Width / 1680, available.Height / 980);
                surface.Width = available.Width / scale;
                surface.Height = available.Height / scale;
                base.MeasureOverride(available);
                return available;
            }
            return base.MeasureOverride(available);
        }
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
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(row);
        var updates = new Button { Content = "Updates · " + AppUpdateLifecycle.Version, Tag = "software-updates", VerticalAlignment = VerticalAlignment.Center };
        updates.Click += (_, _) => OpenUpdates();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        browserStatusButton.Click += (_, _) => OpenBrowserControl();
        UpdateMcpStatus(0);
        actions.Children.Add(mcpIndicator);
        actions.Children.Add(browserStatusButton);
        var appearance = new ComboBox { ItemsSource = Appearance.Choices, SelectedItem = Appearance.Choice,
            Tag = "appearance", VerticalAlignment = VerticalAlignment.Center, MinWidth = 108 };
        Avalonia.Automation.AutomationProperties.SetName(appearance, "Appearance");
        ToolTip.SetTip(appearance, "Appearance");
        appearance.SelectionChanged += (_, _) =>
        {
            if (appearance.SelectedItem is not string choice || Avalonia.Application.Current is not App app) return;
            Appearance.Select(app, choice);
            try { Appearance.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { status.Text = "Could not save appearance preference."; }
        };
        actions.Children.Add(appearance);
        actions.Children.Add(updates);
        AddColumn(header, actions, 1);
        return new Border { Background = Appearance.Glass, Child = header, Padding = new Thickness(16, 10), BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
}
