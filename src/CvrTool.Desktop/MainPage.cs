// Desktop / Main — equal-width input/output panels and shared strip dimensions.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia.Controls;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private const double MeterHeight = 280;
    private const double ChannelWidth = 160;
    private const string StripRows = "28,280,28,40,28,28,28,28";
    private Control MainPage()
    {
        var sections = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        var inputs = Card(ChannelLevels());
        inputs.Tag = "main-inputs";
        var outputs = Card(OutputMonitoring());
        outputs.Tag = "main-outputs";
        inputs.Width = outputs.Width = channels.Count * (ChannelWidth + 12) + 38;
        sections.Children.Add(inputs);
        sections.Children.Add(outputs);
        return sections;
    }
}
