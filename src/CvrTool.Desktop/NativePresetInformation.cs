// Desktop / Native preset information
// Purpose: display imported source labels and input-mute state absent from DSP editors.
// Units: per-channel names, dB/ms source alignment and configured ohms.
// State: inspection only; changing normal DSP controls preserves this metadata.
// Scope: decoded B118/B119 data; source selection, links and DEQ remain preserved.
using Avalonia.Controls;
using Avalonia.Layout;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control? NativePresetInformation()
    {
        var metadata = service.GetNativePresetMetadata(deviceId);
        if (metadata is null) return null;
        var panel = new StackPanel { Spacing = 14, Tag = "native-preset-information" };
        panel.Children.Add(Heading("Native preset information · Read-only"));
        panel.Children.Add(Text("Input mute is separate from routing mute. Configured load is separate from measured load.", 12, Muted));
        foreach (var channel in metadata.Channels)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(Heading($"Channel {(char)('A' + channel.Channel - 1)}"));
            var names = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 18 };
            AddColumn(names, Labeled("Input name", Text(string.IsNullOrWhiteSpace(channel.InputName) ? "—" : channel.InputName, 13)), 0);
            AddColumn(names, Labeled("Speaker name", Text(string.IsNullOrWhiteSpace(channel.SpeakerName) ? "—" : channel.SpeakerName, 13)), 1);
            AddColumn(names, Labeled("Input mute", Text(channel.InputMuted is null ? "Unknown" : channel.InputMuted.Value ? "Muted" : "Unmuted", 13)), 2);
            AddColumn(names, Labeled("Configured load", Text(Known(channel.ConfiguredLoadOhms, " Ω"), 13)), 3);
            row.Children.Add(names);
            string Alignment(string source, SourceAlignment value) => source + ": " + Known(value.GainDb, " dB") + " / " + Known(value.DelayMs, " ms");
            row.Children.Add(Text("Source alignment · " + Alignment("Analog", channel.Analog) + " · " + Alignment("Dante", channel.Dante) + " · " + Alignment("AES", channel.Aes), 12, Muted));
            panel.Children.Add(row);
        }
        return Card(panel);
    }
}
