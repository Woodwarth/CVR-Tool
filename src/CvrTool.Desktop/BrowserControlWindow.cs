// Desktop / Browser control lifecycle
// Purpose: start the embedded iPad/LAN interface and show its connection link.
// Units: selected local IPv4 address and TCP port 8787.
// State: explicitly enabled per desktop session; closing the app stops the server.
// Scope: no router configuration, automatic firewall changes or cloud access.
using System.Net;
using Avalonia;
using Avalonia.Controls;
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

    private void OpenBrowserControl()
    {
        if (browserWindow is not null) { browserWindow.Activate(); return; }
        browserServer ??= new BrowserControlServer(service);
        var server = browserServer;
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        content.Children.Add(Heading("Browser control"));
        content.Children.Add(Text("Open this link in Safari on your iPad. Both devices must be on the same local network.", 14, Muted));
        var interfaces = BrowserControlServer.Interfaces();
        var network = new ComboBox { ItemsSource = interfaces, SelectedIndex = interfaces.Count > 0 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "browser-interface" };
        content.Children.Add(Field("Local network", network));
        var link = new TextBox { IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Tag = "browser-url" };
        var message = Text("", 13, Muted);
        var start = new Button { Content = "Start browser control", Tag = "browser-start" };
        var stop = new Button { Content = "Stop browser control", Tag = "browser-stop" };
        var copy = new Button { Content = "Copy connection link", Tag = "browser-copy" };
        void Refresh()
        {
            var running = server.Address is not null;
            start.IsEnabled = !running && interfaces.Count > 0;
            network.IsEnabled = !running;
            stop.IsEnabled = copy.IsEnabled = running;
            link.Text = server.ConnectionUrl ?? "";
            message.Text = running ? "Running · The connection link grants control. Keep it private." : "Stopped · Start to allow local browser access.";
        }
        start.Click += async (_, _) =>
        {
            if (network.SelectedItem is not BrowserInterface selected) return;
            start.IsEnabled = false;
            try { await server.StartAsync(IPAddress.Parse(selected.Address)); Refresh(); }
            catch (Exception ex) { Refresh(); message.Text = ex.Message; }
        };
        stop.Click += async (_, _) => { stop.IsEnabled = false; await server.DisposeAsync(); Refresh(); };
        copy.Click += async (_, _) => { if (Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, server.ConnectionUrl); };
        content.Children.Add(link);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(start); actions.Children.Add(stop); content.Children.Add(actions);
        content.Children.Add(copy); content.Children.Add(message);
        content.Children.Add(Text("Allow private-network access if Windows asks. Internet access is not required. This does not change amplifier discovery.", 12, Muted));
        browserWindow = EditorWindow("Browser control · iPad", content, 700, 420);
        browserWindow.Closed += (_, _) => browserWindow = null;
        Refresh(); browserWindow.Show(this);
    }

    private void InitializeBrowserControl()
    {
        // Backend writes from the browser invalidate and refresh the same desktop state.
        browserServer = new BrowserControlServer(service);
        browserServer.Changed += () => Dispatcher.UIThread.Post(ReloadSession);
        Closed += (_, _) => browserServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
