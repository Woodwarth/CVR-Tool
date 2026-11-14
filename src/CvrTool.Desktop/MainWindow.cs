// Desktop / Shell — window layout, device selection and navigation; Application owns behavior.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow : Window
{
    // Theme and window state
    // One service instance is shared by every page in this window.
    private static IBrush Ink => Appearance.Ink;
    private static IBrush Muted => Appearance.Muted;
    private static IBrush Accent => Appearance.Accent;
    private static IBrush Line => Appearance.Line;
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
    private bool liveReadOnly;
    private bool controlWritePending;
    private readonly HashSet<string> knownDevices = new();
    private List<ChannelDraft> channels = [];
    // Window composition
    // Build the shell once; navigation replaces only the active page.
    public MainWindow() : this(new AmplifierService(new DemoBackend()))
    {
    }

    public MainWindow(AmplifierService amplifierService)
    {
        service = amplifierService;
        WindowsTitleBar.Attach(this);
        Title = "CVR Tool · DSP control";
        Width = 1680;
        Height = 980;
        MinWidth = 1008;
        MinHeight = 588;
        Background = Appearance.Canvas;
        Foreground = Ink;
        FontFamily = new FontFamily("Inter, Segoe UI, sans-serif");
        Icon = Branding.WindowIcon();
        Content = BuildShell();
        InitializeBrowserControl();
        Opened += (_, _) => FitInitialWindow();
        Select(deviceId);
        StartLiveRefresh();
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
            var previous = FocusManager?.GetFocusedElement() as Control;
            var tag = previous?.Tag as string;
            RenderPage();
            if (IsActive && tag is not null && previous?.IsFocused != true &&
                FocusManager?.GetFocusedElement() is null)
            {
                this.GetLogicalDescendants().OfType<Control>()
                    .FirstOrDefault(c => Equals(c.Tag,tag) && c.Focusable && c.IsEffectivelyEnabled)?.Focus();
            }
        }
        catch (Exception ex) { status.Text = "Device refresh failed: " + ex.Message; }
    }

    private void RenderPage()
    {
        if (deviceId.Length == 0)
        {
            navigation.Children.Clear(); knownDevices.Clear(); deviceDots.Clear(); tabs.Children.Clear();
            deviceName.Text = ""; nameSlot.IsEnabled = false; deviceActions.Content = null;
            page.Content = Heading("No devices"); liveReadOnly = false;
            return;
        }
        var selectedDevice = service.GetDevice(deviceId);
        liveReadOnly = selectedDevice.Readings?.ReadOnly == true;
        if (liveReadOnly)
        {
            renderedRevision = service.SettingsRevision(deviceId);
            renderedOnline = ConnectionAvailable(selectedDevice);
            renderedReadings = selectedDevice.Readings;
        }
        deviceName.Text = selectedDevice.Capabilities.Model;
        nameSlot.IsEnabled = !controlWritePending && (!liveReadOnly || (ConnectionAvailable(selectedDevice) && selectedDevice.Readings!.CanRename));
        deviceActions.IsEnabled = !controlWritePending && ConnectionAvailable(selectedDevice) && (!liveReadOnly || selectedDevice.Readings!.CanSetDisplayLock || selectedDevice.Readings.CanSetStandby || selectedDevice.Readings.CanSetWink);
        ShowDeviceName();
        RefreshDeviceInfo();
        deviceActions.Content = DeviceActions();
        navigation.Children.Clear();
        deviceDots.Clear();
        var availableDevices = service.ListDevices();
        knownDevices.Clear();
        foreach (var device in availableDevices)
        {
            knownDevices.Add(device.Id);
            var selected = device.Id == deviceId;
            var label = new StackPanel
            {
                Spacing = 4
            };
            var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
            var dot = Text("●", 13, ConnectionColor(device));
            dot.Tag = "device-dot-" + device.Id;
            deviceDots[device.Id] = dot;
            nameRow.Children.Add(dot);
            var primaryName = Text(device.Name, 13);
            primaryName.TextWrapping = TextWrapping.Wrap;
            AddColumn(nameRow, primaryName, 1);
            label.Children.Add(nameRow);
            label.Children.Add(Text(device.Capabilities.Model, 10, Muted));
            var button = new Button
            {
                Content = label,
                Width = 136,
                Tag = device.Id,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 14),
                Background = selected ? Appearance.Selected : Brushes.Transparent,
                BorderBrush = selected ? Accent : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12)
            };
            button.Click += (_, _) => Select(device.Id);
            navigation.Children.Add(button);
        }


        var pageTabs = new List<(string Name, string Symbol)>
        {
            ("Main", "⌂"), ("Inputs", "◉"), ("Outputs", "◖"), ("Presets", "☰")
        };
        foreach (var (name, symbol) in pageTabs)
        {
            var existing = tabs.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, name));
            if (existing is not null)
            {
                existing.Background = activeTab == name ? Appearance.Selected : Brushes.Transparent;
                existing.BorderBrush = activeTab == name ? Accent : Brushes.Transparent;
                continue;
            }
            var button = new Button
            {
                Tag = name,
                Content = symbol + "   " + name,
                MinWidth = 106,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 10),
                VerticalAlignment = VerticalAlignment.Center,
                Background = activeTab == name ? Appearance.Selected : Brushes.Transparent,
                BorderBrush = activeTab == name ? Accent : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12)
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
        if (liveReadOnly && activeTab != "Presets" && page.Content is Control livePage) DisableLiveEdits(livePage, false, ConnectionAvailable(selectedDevice) && selectedDevice.Readings?.CanSetMutes == true && !controlWritePending, ConnectionAvailable(selectedDevice) && selectedDevice.Readings?.CanSetInputGain == true && !controlWritePending, ConnectionAvailable(selectedDevice) && selectedDevice.Readings?.CanSetVerifiedDsp == true && !controlWritePending);
    }

    public void ReloadSession()
    {
        try
        {
            var devices = service.ListDevices();
            var added = devices.FirstOrDefault(d => !d.IsDemo && !knownDevices.Contains(d.Id));
            if (added is not null) { deviceId = added.Id; activeTab = "Main"; }
            else if (!devices.Any(d => d.Id == deviceId)) deviceId = devices.FirstOrDefault()?.Id ?? "";
            if (deviceId.Length == 0) { Render(); return; }
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
        if (liveReadOnly) { CommitLiveMute(change); return; }
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
