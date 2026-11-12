// Desktop / Inputs and routing — per-channel processing, route dials and output trim.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control InputsPage()
    {
        RoutingSnapshot routing;
        try
        {
            routing = service.GetRouting(deviceId);
        }
        catch (NotSupportedException)
        {
            return Card(InputsPanel());
        }

        var content = new StackPanel
        {
            Spacing = 12
        };
        content.Children.Add(Heading($"Inputs · {deviceName.Text}"));
        var matrix = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("130", routing.InputCount))),
            RowDefinitions = new RowDefinitions("32," + string.Join(",", Enumerable.Repeat("110", routing.OutputCount))),
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        for (var input = 1; input <= routing.InputCount; input++)
            AddColumn(matrix, Centered($"Source {input}", 13), input - 1);
        for (var output = 1; output <= routing.OutputCount; output++)
        {
            for (var input = 1; input <= routing.InputCount; input++)
            {
                var state = routing.Cells.Single(c => c.Input == input && c.Output == output);
                var current = state;
                var dial = new RoutingDial
                {
                    DeferGainUntilRelease = liveReadOnly,
                    GainDb = state.GainDb,
                    Muted = state.Muted,
                    Tag = $"route-dial-{input}-{output}",
                    CanEditGain = !liveReadOnly || service.GetDevice(deviceId).Readings?.CanSetVerifiedDsp == true
                };
                Avalonia.Automation.AutomationProperties.SetName(dial, $"Source {input} to output {(char)('A' + output - 1)} gain and mute");
                void Change(double? gainDb = null, bool? muted = null)
                {
                    if (liveReadOnly)
                    { CommitLiveRouteMute(state.Input, state.Output, muted, gainDb); return; }
                    try
                    {
                        current = service.ApplyRouting(service.PlanRouting(new(deviceId, state.Input, state.Output, gainDb, muted)).Id);
                        status.Text = $"Route {state.Input} → {(char)('A' + state.Output - 1)} applied";
                    }
                    catch (Exception ex)
                    {
                        status.Text = "Route not applied: " + ex.Message;
                    }

                    dial.GainDb = current.GainDb;
                    dial.Muted = current.Muted;
                }

                dial.InteractionStarted += () => { if (liveReadOnly) liveEditInteractions++; };
                dial.InteractionEnded += () => { liveEditInteractions = Math.Max(0, liveEditInteractions - 1); };
                dial.MuteRequested += () => Change(muted: !current.Muted);
                dial.GainChanged += value => Change(gainDb: value);
                Grid.SetRow(dial, output);
                AddColumn(matrix, dial, input - 1);
            }
        }

        // Keep the signal-flow grid fixed; the shell scales uniformly on smaller windows.
        var environment = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("156,Auto,156"),
            ColumnSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            Tag = "routing-environment"
        };
        var sourceProcessing = new Grid
        {
            RowDefinitions = new RowDefinitions("32," + string.Join(",", Enumerable.Repeat("110", routing.InputCount))),
            RowSpacing = 8,
            Margin = new Thickness(0, 19)
        };
        var outputLevels = new Grid
        {
            RowDefinitions = new RowDefinitions("32," + string.Join(",", Enumerable.Repeat("110", routing.OutputCount))),
            RowSpacing = 8,
            Margin = new Thickness(0, 19)
        };
        sourceProcessing.Children.Add(Centered("Input processing", 13, Muted));
        outputLevels.Children.Add(Centered("Output gain", 13, Muted));
        for (var row = 1; row <= routing.InputCount; row++)
        {
            var blocks = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            AddColumn(blocks, DelayControl(row), 0);
            AddColumn(blocks, InputEqControl(row), 1);
            Grid.SetRow(blocks, row);
            sourceProcessing.Children.Add(blocks);
        }

        foreach (var output in service.GetDevice(deviceId).Outputs)
        {
            var gain = new GainReadout { Value = output.GainDb, Tag = "routing-output-gain-" + output.Name };
            gain.EditingStarted += () => { if (liveReadOnly) liveEditInteractions++; };
            gain.EditingEnded += () => { liveEditInteractions = Math.Max(0, liveEditInteractions - 1); };
            gain.ValueChanged += value => { channels.Single(c => c.State.Number == output.Number).Gain = value; Commit(new(deviceId, output.Number, GainDb: value)); };
            var label = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Centered("Volume " + output.Name, 13));
            label.Children.Add(gain);
            var block = new Border { Child = label, Height = 72, Padding = new Thickness(6), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            block.Tag = "route-volume-" + output.Name;
            Grid.SetRow(block, output.Number);
            outputLevels.Children.Add(block);
        }

        var connections = new RoutingConnections(routing.OutputCount, 114, 118)
        {
            IsHitTestVisible = false
        };
        Grid.SetColumnSpan(connections, 3);
        environment.Children.Add(connections);
        environment.Children.Add(sourceProcessing);
        AddColumn(environment, Card(matrix), 1);
        AddColumn(environment, outputLevels, 2);
        content.Children.Add(new ScrollViewer { Content = environment, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        return content;
    }

    private static Control RoutingStage(string name, string value)
    {
        var label = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.Children.Add(Centered(name, 12));
        label.Children.Add(Centered(value, 11, Muted));
        return new Border
        {
            Child = label,
            Height = 72,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(5),
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            Background = Brush.Parse("#182129"),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
