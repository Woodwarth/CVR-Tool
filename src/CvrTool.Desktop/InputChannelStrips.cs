// Desktop / Inputs — immediate gain editing with aligned level and clip indicators.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using CvrTool.Core;

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
            var input = service.GetInputProcessing(deviceId, draft.State.Number);
            var inputGain = input.GainDb;
            var editedId = deviceId; var channel = draft.State.Number;
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
                Value = inputGain ?? 0,
                IsHitTestVisible = false
            };
            var slider = new Slider
            {
                Minimum = -80,
                Maximum = 18,
                Value = inputGain ?? 0,
                IsEnabled = inputGain is not null,
                SmallChange = .5,
                TickFrequency = .5, IsSnapToTickEnabled = true,
                Tag = "input-gain-" + draft.State.Name,
                Orientation = Orientation.Vertical,
                Opacity = 0
            };
            AutomationProperties.SetName(slider, "Channel " + draft.State.Name + " gain");
            var overlay = new Grid();
            overlay.Children.Add(face);
            overlay.Children.Add(slider);
            fader.Children.Add(overlay);
            AddColumn(fader, new ChannelMeter(liveReadOnly ? data.InputMeterDb : data.LevelDbFs, liveReadOnly ? -40 : -60) { Width = 24, Height = MeterHeight, Tag = "channel-meter-" + draft.State.Name }, 1);
            var gain = Centered(inputGain is { } knownGain ? knownGain.ToString("0.0")+" dB" : "—",15);
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
            var updatingGain = false;
            var interacting = false;
            void PaintGain(double? value)
            {
                gain.Text = value is { } number ? number.ToString("0.0") + " dB" : "—";
                face.Value = value ?? 0; face.InvalidateVisual();
            }
            void BeginInteraction()
            {
                if (!liveReadOnly || interacting) return;
                interacting = true; liveEditInteractions++;
            }
            async Task CommitGain()
            {
                if (interacting) { interacting = false; liveEditInteractions = Math.Max(0, liveEditInteractions - 1); }
                if (!liveReadOnly || updatingGain || controlWritePending || slider.Value == inputGain) return;
                updatingGain = true;
                try
                {
                    await CommitLiveInput(new(editedId, channel, GainDb: slider.Value), "Input " + draft.State.Name + " gain");
                    inputGain = service.GetInputProcessing(editedId, channel).GainDb;
                    slider.Value = inputGain ?? 0; PaintGain(inputGain);
                }
                finally { updatingGain = false; }
            }
            slider.AddHandler(InputElement.PointerPressedEvent, (_, _) => BeginInteraction(), RoutingStrategies.Tunnel);
            slider.AddHandler(InputElement.PointerReleasedEvent, async (_, _) => await CommitGain(), RoutingStrategies.Bubble, handledEventsToo: true);
            slider.PointerCaptureLost += async (_, _) => { if (interacting) await CommitGain(); };
            slider.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) BeginInteraction(); }, RoutingStrategies.Tunnel);
            slider.KeyUp += async (_, e) => { if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) await CommitGain(); };
            slider.AddHandler(InputElement.PointerWheelChangedEvent, async (_, e) =>
            {
                if (!liveReadOnly || !slider.IsEffectivelyEnabled) return;
                e.Handled = true;
                slider.Value = Math.Clamp(slider.Value + (e.Delta.Y > 0 ? .5 : -.5), -80, 18);
                await CommitGain();
            }, RoutingStrategies.Tunnel);
            slider.LostFocus += async (_, _) => { if (interacting) await CommitGain(); };
            slider.ValueChanged += (_, _) =>
            {
                if (updatingGain) return;
                PaintGain(slider.Value);
                if (liveReadOnly) return;
                updatingGain = true;
                try
                {
                    var plan = service.PlanInputProcessing(new(editedId, channel, GainDb: slider.Value));
                    inputGain = service.ApplyInputProcessing(plan.Id).GainDb;
                    status.Text = $"Input {draft.State.Name} gain applied";
                }
                catch (Exception ex) { status.Text = ex.Message; }
                finally { slider.Value = inputGain ?? 0; PaintGain(inputGain); updatingGain = false; }
            };
            Grid.SetRow(fader, 1);
            strip.Children.Add(fader);
            var clip = StatusLight("Clip", liveReadOnly ? data.InputClip : data.Status?.Clip);
            clip.Tag = "channel-clip-" + draft.State.Name;
            Grid.SetRow(clip, 2);
            strip.Children.Add(clip);
            Grid.SetRow(gain, 3);
            strip.Children.Add(gain);
            var inputMute = MuteSwitch(input.Muted == true, "Input " + draft.State.Name, async value =>
            {
                if (liveReadOnly) await CommitLiveInput(new(editedId, channel, Muted: value), "Input " + draft.State.Name + " mute");
                else
                {
                    try { service.ApplyInputProcessing(service.PlanInputProcessing(new(editedId, channel, Muted: value)).Id); Render(); }
                    catch (Exception ex) { status.Text = ex.Message; Render(); }
                }
            });
            inputMute.Tag = "mute-input-" + draft.State.Name;
            inputMute.IsEnabled = input.Muted is not null && (!liveReadOnly || service.GetDevice(editedId).Readings?.CanSetInputMute == true);
            Grid.SetRow(inputMute, 4); strip.Children.Add(inputMute);
            strips.Children.Add(ChannelGroup(strip, "input-channel-" + draft.State.Name));
        }

        content.Children.Add(strips);
        return content;
    }
}
