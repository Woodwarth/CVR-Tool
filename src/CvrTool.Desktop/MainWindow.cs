// Desktop / Shell — window layout, device selection and navigation; Application owns behavior.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow : Window
{
    // Theme and window state
    // One service instance is shared by every page in this window.
    private static readonly IBrush Ink = Brush.Parse("#EDF2F5"), Muted = Brush.Parse("#96A5AE"), Accent = Brush.Parse("#00CEBD"), Line = Brush.Parse("#46525D");
    private readonly AmplifierService service;
    private readonly StackPanel navigation = new()
    {
        Spacing = 8
    };
    private readonly WrapPanel tabs = new()
    {
        Orientation = Orientation.Horizontal
    };
    private readonly ContentControl page = new();
    private readonly TextBlock status = Text("Local session · No amplifier connected", 13, Muted);
    private readonly TextBlock deviceName = Text("", 16);
    private readonly TextBox nameEditor = new()
    {
        MaxLength = 32,
        Tag = "device-name",
        MinWidth = 120
    };
    private readonly ContentControl nameSlot = new(), deviceActions = new();
    private string deviceId = "demo-1004", activeTab = "Main";
    private string? nativePresetName;
    private List<ChannelDraft> channels = [];
    // Window composition
    // Build the shell once; navigation replaces only the active page.
    public MainWindow() : this(new AmplifierService(new DemoBackend()))
    {
    }

    public MainWindow(AmplifierService amplifierService)
    {
        service = amplifierService;
        Title = "CVR Tool · DSP control";
        Width = 1680;
        Height = 980;
        MinWidth = 1008;
        MinHeight = 588;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.Parse("#252D35"), 0),
                new(Color.Parse("#10171E"), 1)
            }
        };
        Foreground = Ink;
        FontFamily = new FontFamily("Inter, Segoe UI, sans-serif");
        Icon = Branding.WindowIcon();
        Content = BuildShell();
        Opened += (_, _) => FitInitialWindow();
        Select(deviceId);
    }

    // Device navigation
    // Reload drafts from saved state when switching devices.
    private void Select(string id)
    {
        deviceId = id;
        channels = service.GetDevice(id).Outputs.Select(c => new ChannelDraft(c)).ToList();
        status.Text = service.GetDevice(id).IsDemo ? "Local session · " + service.GetDevice(id).Capabilities.Model : "CVR UDP · " + id;
        Render();
    }

    private void Render()
    {
        try
        {
            RenderPage();
        }
        catch (Exception ex) { status.Text = "Device refresh failed: " + ex.Message; }
    }

    private void RenderPage()
    {
        deviceName.Text = service.GetDevice(deviceId).Capabilities.Model;
        ShowDeviceName();
        RefreshDeviceInfo();
        deviceActions.Content = DeviceActions();
        navigation.Children.Clear();
        foreach (var device in service.ListDevices())
        {
            var selected = device.Id == deviceId;
            var label = new StackPanel
            {
                Spacing = 4
            };
            label.Children.Add(Text("●  " + device.Capabilities.Model, 13));
            label.Children.Add(Text(device.Name, 10, Muted));
            var button = new Button
            {
                Content = label,
                Width = 136,
                Tag = device.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 14),
                Background = selected ? Brush.Parse("#006F70") : Brushes.Transparent,
                BorderBrush = selected ? Accent : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5)
            };
            button.Click += (_, _) => Select(device.Id);
            navigation.Children.Add(button);
        }

        tabs.Children.Clear();
        foreach (var (name, symbol) in new[]
        {
            ("Main", "⌂"),
            ("Inputs", "◉"),
            ("Outputs", "◖"),
            ("Presets", "☰")
        }

        )
        {
            var button = new Button
            {
                Content = symbol + "   " + name,
                MinWidth = 106,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 10),
                VerticalAlignment = VerticalAlignment.Center,
                Background = activeTab == name ? Brush.Parse("#007F7D") : Brushes.Transparent,
                BorderBrush = activeTab == name ? Accent : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(4, 4, 0, 0)
            };
            button.Click += (_, _) =>
            {
                activeTab = name;
                Render();
            };
            tabs.Children.Add(button);
        }

        page.Content = activeTab switch
        {
            "Inputs" => InputsPage(),
            "Outputs" => OutputsPage(),
            "Presets" => PresetsPage(),
            _ => MainPage()
        };
    }

    public void ReloadSession()
    {
        try
        {
            if (!service.ListDevices().Any(d => d.Id == deviceId))
                deviceId = "demo-1004";
            channels = service.GetDevice(deviceId).Outputs.Select(c => new ChannelDraft(c)).ToList();
            Render();
            status.Text = "Shared session updated via MCP";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    // Application bridge
    // Presentation code never bypasses shared validation or revision checks.
    private void Apply(ChannelDraft draft) => Commit(new(deviceId, draft.State.Number, draft.Gain, draft.Muted, draft.Delay, draft.Inverted));
    private void ApplyMute(ChannelDraft draft) => Commit(new(deviceId, draft.State.Number, Muted: draft.Muted));
    private void Commit(ChannelChange change)
    {
        try
        {
            service.Apply(service.Plan(change).Id);
            status.Text = $"Output {(char)('A' + change.Channel - 1)} applied";
        }
        catch (Exception ex)
        {
            status.Text = "Change not applied: " + ex.Message;
        }
    }

    private sealed class ChannelDraft(ChannelState state)
    {
        public ChannelState State { get; } = state;
        public double Gain { get; set; } = state.GainDb;
        public double Delay { get; set; } = state.DelayMs;
        public bool Muted { get; set; } = state.Muted;
        public bool Inverted { get; set; } = state.Inverted;
    }
}
