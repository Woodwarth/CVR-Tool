// Desktop / Device summary
// Purpose: show compact identity and connection information in two rows.
// Input: authoritative application snapshots and per-device operational state.
// State: model/name controls retain their existing edit behavior.
// Notes: extended five-line headers refer to source documentation, not UI metadata.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, TextBlock> infoValues = new();
    private Control DeviceHeader()
    {
        Control Value(string key, string label)
        {
            var value = Text("—", 13, Muted);
            value.Tag = "info-" + key;
            infoValues[key] = value;
            return Labeled(label, value);
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.1*,1.1*,1*,Auto"), ColumnSpacing = 22 };
        var model = new StackPanel { Spacing = 8 };
        model.Children.Add(Labeled("Model", deviceName));
        model.Children.Add(Value("ip", "IP address"));
        grid.Children.Add(model);
        var name = new StackPanel { Spacing = 8 };
        name.Children.Add(Labeled("Name", nameSlot));
        name.Children.Add(Value("preset", "Preset"));
        AddColumn(grid, name, 1);
        var connection = new StackPanel { Spacing = 8 };
        connection.Children.Add(Value("channels", "Channels"));
        connection.Children.Add(Value("connection", "Connection"));
        AddColumn(grid, connection, 2);
        AddColumn(grid, deviceActions, 3);
        var card = Card(grid);
        card.Tag = "info-header";
        return card;
    }
    private void RefreshDeviceInfo()
    {
        var device = service.GetDevice(deviceId);
        infoValues["ip"].Text = device.IsDemo ? "—" : device.Id.Split(':')[1];
        infoValues["preset"].Text = nativePresetName ?? "Local settings";
        infoValues["channels"].Text = device.Outputs.Count + " in / " + device.Outputs.Count + " out";
        infoValues["connection"].Text = device.IsDemo ? "Local session" : "CVR UDP · Readback";
    }
}
