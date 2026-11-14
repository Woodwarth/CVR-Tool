// Desktop / Browser control lifecycle
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: start the embedded iPad/LAN interface and show its connection link.
// Units: selected local IPv4 address and TCP port 8787.
// State: explicitly enabled per desktop session; closing the app stops the server.
// Scope: no router configuration, automatic firewall changes or cloud access.
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CvrTool.Web;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private BrowserControlServer? browserServer;
    private Window? browserWindow;
    private readonly Button browserStatusButton = new()
    {
        Content = "Browser control · Off", Tag = "browser-control", Width = 52, Height = 34, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private void UpdateBrowserStatus()
    {
        var running = browserServer?.IsRunning == true;
        var connected = browserServer?.ClientConnected == true;
        browserStatusButton.Content = new Avalonia.Controls.Shapes.Path
        {
            Data = Avalonia.Media.Geometry.Parse("M 10,1 A 9,9 0 1 0 10,19 A 9,9 0 1 0 10,1 M 1,10 H 19 M 10,1 C 4,5 4,15 10,19 C 16,15 16,5 10,1"),
            Stroke = connected ? Avalonia.Media.Brushes.LimeGreen : running ? Accent : Muted, StrokeThickness = 1.5, Width = 21, Height = 21
        };
        AutomationProperties.SetName(browserStatusButton, "Browser control · " + (connected ? "Connected" : running ? "On" : "Off"));
        browserStatusButton.Foreground = running ? Accent : Muted;
        browserStatusButton.BorderBrush = running ? Accent : Line;
        browserStatusButton.Background = running ? Appearance.Selected : Appearance.Surface;
    }

    private void OpenBrowserControl()
    {
        if (browserWindow is not null) { browserWindow.Activate(); return; }
        browserServer ??= new BrowserControlServer(service);
        var server = browserServer;
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        content.Children.Add(Heading("Browser control"));
        content.Children.Add(Text("Scan the QR code with your iPad camera to open browser control. Both devices must be on the same local network.", 14, Muted));
        var interfaces = BrowserControlServer.Interfaces().Append(new BrowserInterface("All interfaces", "0.0.0.0")).ToList();
        var network = new ComboBox { ItemsSource = interfaces, SelectedIndex = interfaces.Count > 0 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "browser-interface" };
        content.Children.Add(Field("Local network", network));
        var link = new TextBox { IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Tag = "browser-url", MinHeight = 80 };
        var qr = new BrowserConnectionQrCode { Width = 280, Height = 280, IsVisible = false, Tag = "browser-qr" };
        AutomationProperties.SetName(qr, "Scan to connect to browser control");
        var pairing = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*"), ColumnSpacing = 20 };
        pairing.Children.Add(qr);
        var details = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(Text("Scan to connect", 20));
        details.Children.Add(Text("Open the link with your iPad camera, or copy it into Safari.", 14, Muted));
        var links = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        links.SelectionChanged += (_, _) =>
        {
            if (links.SelectedIndex >= 0 && links.SelectedIndex < server.ConnectionUrls.Length)
            {
                link.Text = server.ConnectionUrls[links.SelectedIndex]; qr.SetConnectionUrl(link.Text);
            }
        };
        details.Children.Add(links);
        details.Children.Add(link);
        var message = Text("", 13, Muted);
        var start = new Button { Content = "Start browser control", Tag = "browser-start" };
        var stop = new Button { Content = "Stop browser control", Tag = "browser-stop" };
        var copy = new Button { Content = "Copy connection link", Tag = "browser-copy" };
        details.Children.Add(copy);
        details.Children.Add(Text("The QR code includes the private access key. Share it only with people who may control this session.", 12, Muted));
        AddColumn(pairing, details, 1);
        void Refresh()
        {
            var running = server.Address is not null;
            start.IsEnabled = !running && interfaces.Count > 0;
            network.IsEnabled = !running;
            stop.IsEnabled = copy.IsEnabled = running;
            link.Text = server.ConnectionUrl ?? "";
            qr.SetConnectionUrl(server.ConnectionUrl);
            links.IsVisible = server.ConnectionUrls.Length > 1;
            if (links.IsVisible)
            {
                links.ItemsSource = server.ConnectionUrls.Select(url => new Uri(url).Authority).ToArray();
                links.SelectedIndex = 0;
            }
            pairing.IsVisible = running;
            message.Text = running ? (server.ClientConnected ? "Running · 1/1 browser connected" : "Running · Waiting for browser (0/1)") : "Stopped · Start to allow local browser access.";
        }
        start.Click += async (_, _) =>
        {
            if (network.SelectedItem is not BrowserInterface selected) return;
            start.IsEnabled = false;
            try { await server.StartAsync(IPAddress.Parse(selected.Address)); Refresh(); }
            catch (Exception ex) { Refresh(); message.Text = ex.Message; }
        };
        stop.Click += async (_, _) => { stop.IsEnabled = false; await server.DisposeAsync(); Refresh(); };
        copy.Click += async (_, _) => { if (Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, link.Text); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(start); actions.Children.Add(stop); content.Children.Add(actions);
        content.Children.Add(pairing); content.Children.Add(message);
        content.Children.Add(Text("Allow private-network access if Windows asks. Internet access is not required. This does not change amplifier discovery.", 12, Muted));
        browserWindow = EditorWindow("Browser control · iPad", new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 760, 650);
        browserWindow.MinWidth = 680;
        void StatusChanged() => Dispatcher.UIThread.Post(() => { if (browserWindow is not null) Refresh(); });
        server.StatusChanged += StatusChanged;
        browserWindow.Closed += (_, _) => { server.StatusChanged -= StatusChanged; browserWindow = null; };
        Refresh(); browserWindow.Show(this);
    }

    private void InitializeBrowserControl()
    {
        // Backend writes from the browser invalidate and refresh the same desktop state.
        browserServer = new BrowserControlServer(service);
        browserServer.Changed += () => Dispatcher.UIThread.Post(ReloadSession);
        browserServer.StatusChanged += () => Dispatcher.UIThread.Post(UpdateBrowserStatus);
        UpdateBrowserStatus();
        Closed += (_, _) => browserServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
