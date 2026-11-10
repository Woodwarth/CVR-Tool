// Desktop / Outputs — local channel drafts and validated apply actions.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia.Controls;
using Avalonia.Layout;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control OutputsPage()
    {
        var panel = new StackPanel
        {
            Spacing = 18
        };
        panel.Children.Add(Heading("Outputs · " + deviceName.Text));
        panel.Children.Add(Text("Output signal chain · Edit gain, delay, polarity and mute.", 13, Muted));
        foreach (var draft in channels)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("70,*,*,150,140,Auto"),
                ColumnSpacing = 18
            };
            row.Children.Add(Text("Output " + draft.State.Name, 14));
            var gain = Number(-80, 18, 1, (decimal)draft.Gain);
            gain.ValueChanged += (_, _) =>
            {
                if (gain.Value is { } v)
                    draft.Gain = (double)v;
            };
            AddColumn(row, Field("Gain · dB", gain), 1);
            var delay = Number(0, 100, .1m, (decimal)draft.Delay);
            delay.ValueChanged += (_, _) =>
            {
                if (delay.Value is { } v)
                    draft.Delay = (double)v;
            };
            AddColumn(row, Field("Delay · ms", delay), 2);
            var mute = MuteSwitch(draft.Muted, "Output " + draft.State.Name, value => draft.Muted = value);
            mute.Tag = "mute-editor-" + draft.State.Name;
            AddColumn(row, mute, 3);
            var inverted = new CheckBox
            {
                Content = "Inverted",
                IsChecked = draft.Inverted
            };
            inverted.IsCheckedChanged += (_, _) => draft.Inverted = inverted.IsChecked == true;
            AddColumn(row, inverted, 4);
            var apply = new Button
            {
                Content = "Apply",
                VerticalAlignment = VerticalAlignment.Center
            };
            apply.Click += (_, _) =>
            {
                if (gain.Value is null || delay.Value is null)
                {
                    status.Text = "Enter gain and delay.";
                    return;
                }

                Apply(draft);
            };
            AddColumn(row, apply, 5);
            var channelPanel = new StackPanel
            {
                Spacing = 16
            };
            var chain = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            chain.Children.Add(Stage("Source", "Routing", false));
            chain.Children.Add(Text("→", 16, Accent));
            foreach (var block in new[]
            {
                "FIR",
                "EQ",
                "Limiter",
                "Mode",
                "Noise Gate",
                "Stereo / Profile"
            }

            )
                chain.Children.Add(Stage(block, "Not available", false));
            channelPanel.Children.Add(chain);
            channelPanel.Children.Add(row);
            panel.Children.Add(Card(channelPanel));
        }

        panel.Children.Add(Text("Edit ranges: −80 to +18 dB · 0 to 100 ms", 12, Muted));
        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }
}
