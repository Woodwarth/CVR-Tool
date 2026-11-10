// Desktop / Network — offline target editor; no connection or discovery actions.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CvrTool.Application;
using CvrTool.Protocol;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private NetworkTarget networkDraft = new("Amplifier", "");
    private readonly NetworkSetupService networkSetup = new();
    private Control NetworkPage()
    {
        var content = new StackPanel
        {
            Spacing = 18
        };
        content.Children.Add(Heading("Network · Connection setup"));
        content.Children.Add(Text("Configure and save a manual target locally. Connect only to the amplifier address you enter.", 13, Muted));
        var name = new TextBox
        {
            Text = networkDraft.Name,
            MaxLength = 80
        };
        var remote = new TextBox
        {
            Text = networkDraft.RemoteAddress,
            PlaceholderText = "Amplifier IPv4 address",
            MaxLength = 15
        };
        var local = new TextBox
        {
            Text = networkDraft.LocalAddress,
            PlaceholderText = "Local interface IPv4 address",
            MaxLength = 15
        };
        var remotePort = Integer(networkDraft.RemotePort, 1, 65535);
        var localPort = Integer(networkDraft.LocalPort, 0, 65535);
        var timeout = Integer(networkDraft.TimeoutMs, 50, 30000);
        var form = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnSpacing = 24,
            RowSpacing = 18
        };
        form.Children.Add(Field("Target name", name));
        AddColumn(form, Field("Response timeout · ms", timeout), 1);
        var remoteField = Field("Target address · IPv4", remote);
        Grid.SetRow(remoteField, 1);
        form.Children.Add(remoteField);
        var remotePortField = Field("Target UDP port (observed: 45455)", remotePort);
        Grid.SetRow(remotePortField, 1);
        AddColumn(form, remotePortField, 1);
        var localField = Field("Local interface address · IPv4", local);
        Grid.SetRow(localField, 2);
        form.Children.Add(localField);
        var localPortField = Field("Local UDP port (observed: 45454, 0 = automatic)", localPort);
        Grid.SetRow(localPortField, 2);
        AddColumn(form, localPortField, 1);
        content.Children.Add(Card(form));
        var message = Text("Not validated", 14, Accent);
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
        };
        NetworkTarget ReadForm()
        {
            if (remotePort.Value is null || localPort.Value is null || timeout.Value is null)
                throw new ArgumentException("Enter ports and timeout.");
            if (new[]
            {
                remotePort.Value.Value,
                localPort.Value.Value,
                timeout.Value.Value
            }.Any(v => v != decimal.Truncate(v)))
                throw new ArgumentException("Ports and timeout must be whole numbers.");
            return new(name.Text ?? "", remote.Text ?? "", (int)remotePort.Value.Value, local.Text ?? "", (int)localPort.Value.Value, (int)timeout.Value.Value);
        }

        void RememberDraft()
        {
            networkDraft = new(name.Text ?? "", remote.Text ?? "", (int)(remotePort.Value ?? 45455), local.Text ?? "", (int)(localPort.Value ?? 45454), (int)(timeout.Value ?? 1000));
        }

        foreach (var field in new[]
        {
            name,
            remote,
            local
        }

        )
            field.TextChanged += (_, _) => RememberDraft();
        foreach (var field in new[]
        {
            remotePort,
            localPort,
            timeout
        }

        )
            field.ValueChanged += (_, _) => RememberDraft();
        var validate = new Button
        {
            Content = "Validate configuration"
        };
        validate.Click += (_, _) =>
        {
            try
            {
                var plan = networkSetup.Prepare(ReadForm());
                networkDraft = plan.Target;
                message.Text = plan.Status;
                status.Text = "Network target validated offline · No packets sent";
            }
            catch (Exception ex)
            {
                message.Text = "Invalid configuration: " + ex.Message;
            }
        };
        var save = new Button
        {
            Content = "Save locally"
        };
        save.Click += async (_, _) =>
        {
            try
            {
                var target = ReadForm();
                await networkSetup.SaveAsync(target);
                networkDraft = networkSetup.Prepare(target).Target;
                message.Text = "Target saved locally";
            }
            catch (Exception ex)
            {
                message.Text = "Save failed: " + ex.Message;
            }
        };
        var load = new Button
        {
            Content = "Load saved target"
        };
        load.Click += async (_, _) =>
        {
            try
            {
                var plan = await networkSetup.LoadAsync();
                if (plan is null)
                {
                    message.Text = "No network target saved yet.";
                    return;
                }

                var target = plan.Target;
                name.Text = target.Name;
                remote.Text = target.RemoteAddress;
                local.Text = target.LocalAddress;
                remotePort.Value = target.RemotePort;
                localPort.Value = target.LocalPort;
                timeout.Value = target.TimeoutMs;
                networkDraft = target;
                message.Text = plan.Status;
            }
            catch (Exception ex)
            {
                message.Text = "Load failed: " + ex.Message;
            }
        };
        actions.Children.Add(validate);
        actions.Children.Add(save);
        actions.Children.Add(load);
        var connect = new Button { Content = "Connect", Tag = "connect-amplifier" };
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false;
            try
            {
                var target = ReadForm();
                var device = await Task.Run(() => service.Connect(target));
                Select(device.Id);
            }
            catch (Exception ex) { message.Text = "Connection failed: " + ex.Message; }
            finally { connect.IsEnabled = true; }
        };
        actions.Children.Add(connect);
        if (!service.GetDevice(deviceId).IsDemo)
        {
            var disconnect = new Button { Content = "Disconnect" };
            disconnect.Click += (_, _) => { service.Disconnect(deviceId); Select("demo-1004"); };
            actions.Children.Add(disconnect);
        }
        foreach (var action in actions.Children)
            action.Margin = new Thickness(0, 0, 10, 6);
        content.Children.Add(actions);
        content.Children.Add(message);
        content.Children.Add(Text("Manual UDP peer · Close other controllers if they occupy the selected local port.", 12, Muted));
        content.Children.Add(Text("Writes are followed by readback. A timeout may mean that a change applied without confirmation.", 12, Muted));
        content.Children.Add(Text("Local configuration: " + networkSetup.ConfigurationPath, 11, Muted));
        return new ScrollViewer
        {
            Content = content
        };
    }

    private static NumericUpDown Integer(int value, int minimum, int maximum) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = 1,
        FormatString = "0",
        AllowSpin = true
    };
}
