// Desktop / Device summary
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
        model.Children.Add(Labeled("Name", nameSlot));
        model.Children.Add(Value("ip", "IP address"));
        grid.Children.Add(model);
        var name = new StackPanel { Spacing = 8 };
        name.Children.Add(Labeled("Model", deviceName));
        name.Children.Add(Value("preset", "Preset"));
        AddColumn(grid, name, 1);
        var connection = new StackPanel { Spacing = 8 };
        connection.Children.Add(Value("channels", "Channels"));
        connection.Children.Add(Value("connection", "Connection"));
        connection.Children.Add(Value("runtime", "Operating time"));
        AddColumn(grid, connection, 2);
        AddColumn(grid, deviceActions, 3);
        var card = Card(grid);
        card.Tag = "info-header";
        return card;
    }
    private void RefreshDeviceInfo()
    {
        var device = service.GetDevice(deviceId);
        infoValues["ip"].Text = device.IsDemo ? "—" : device.Id.Split(':').ElementAtOrDefault(1) ?? "—";
        infoValues["preset"].Text = device.Readings?.CurrentPreset ?? nativePresetName ?? (device.IsDemo ? "Local settings" : "—");
        infoValues["runtime"].Text = CvrTool.Application.DeviceMetadataCodec.FormatOperatingTime(device.Readings?.OperatingMinutes);
        infoValues["channels"].Text = device.Outputs.Count + " in / " + device.Outputs.Count + " out";
        infoValues["connection"].Text = device.IsDemo ? "Local session" : device.Readings?.ReadOnly == true ?
            (device.Readings.Error is null ? "Live · Verified editing" : "Offline · Cached") : "CVR UDP · Readback";
    }
}
