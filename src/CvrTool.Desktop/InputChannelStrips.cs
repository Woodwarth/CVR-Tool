// Desktop / Inputs — immediate gain editing with aligned level and clip indicators.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control ChannelLevels()
    {
        var content = new StackPanel
        {
            Spacing = 12
        };
        content.Children.Add(Heading("Inputs"));
        var strips = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        var telemetry = service.GetTelemetry(deviceId);
        for (var i = 0; i < channels.Count; i++)
        {
            var draft = channels[i];
            var data = telemetry.Single(t => t.Channel == draft.State.Number);
            var strip = new Grid
            {
                RowDefinitions = new RowDefinitions(StripRows),
                RowSpacing = 6
            };
            strip.Children.Add(Centered(draft.State.Name, 17));
            var fader = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("66,24"),
                Height = MeterHeight,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var face = new FaderFace
            {
                Value = draft.Gain,
                IsHitTestVisible = false
            };
            var slider = new Slider
            {
                Minimum = -80,
                Maximum = 18,
                Value = draft.Gain,
                SmallChange = .5,
                Orientation = Orientation.Vertical,
                Opacity = 0
            };
            AutomationProperties.SetName(slider, "Channel " + draft.State.Name + " gain");
            var overlay = new Grid();
            overlay.Children.Add(face);
            overlay.Children.Add(slider);
            fader.Children.Add(overlay);
            AddColumn(fader, new ChannelMeter(data.LevelDbFs) { Width = 24, Height = MeterHeight, Tag = "channel-meter-" + draft.State.Name }, 1);
            var gain = Centered(draft.Gain.ToString("0.0") + " dB", 15);
            gain.Tag = "gain-" + draft.State.Name;
            slider.GotFocus += (_, _) =>
            {
                face.HasFocus = true;
                face.InvalidateVisual();
            };
            slider.LostFocus += (_, _) =>
            {
                face.HasFocus = false;
                face.InvalidateVisual();
            };
            // Both input methods commit through the shared service; no separate Apply button.
            slider.ValueChanged += (_, _) =>
            {
                draft.Gain = slider.Value;
                gain.Text = slider.Value.ToString("0.0") + " dB";
                face.Value = slider.Value;
                face.InvalidateVisual();
                Commit(new(deviceId, draft.State.Number, GainDb: slider.Value));
            };
            Grid.SetRow(fader, 1);
            strip.Children.Add(fader);
            var clip = StatusLight("Clip", data.Status?.Clip);
            clip.Tag = "channel-clip-" + draft.State.Name;
            Grid.SetRow(clip, 2);
            strip.Children.Add(clip);
            Grid.SetRow(gain, 3);
            strip.Children.Add(gain);
            strips.Children.Add(ChannelGroup(strip, "input-channel-" + draft.State.Name));
        }

        content.Children.Add(strips);
        return content;
    }
}
