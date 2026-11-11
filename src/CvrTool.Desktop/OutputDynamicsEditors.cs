// Desktop / Output dynamics editors
// Purpose: edit per-output Low-Z/70V/100V, per-channel gate and peak/RMS limiting.
// Units: output volts, integer ms, dB and release/attack multiplier.
// State: one window per device/channel/kind; plans reject concurrent changes.
// Scope: drafts commit through Application; device maximum voltages stay read-only.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private void CommitDynamics(OutputDynamicsChange change, OutputDynamicsState original)
    {
        var plan = service.PlanOutputDynamics(change);
        if (plan.Before != original)
            throw new InvalidOperationException("Output dynamics changed externally. Reopen this editor.");
        service.ApplyOutputDynamics(plan.Id);
        ReloadSession();
        status.Text = "Output settings applied";
    }
    private void OpenModeEditor(int channel)
    {
        var editedId = deviceId;
        if (ActivateInputEditor(editedId, channel, "mode"))
            return;
        var original = service.GetOutputDynamics(editedId, channel);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18 };
        panel.Children.Add(Heading($"Mode · Output {(char)('A' + channel - 1)}"));
        var mode = new ComboBox { ItemsSource = OutputLoadModes.Options, SelectedIndex = original.LoadMode is >= 0 and <= 2 ? original.LoadMode.Value : -1, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "output-mode-choice" };
        panel.Children.Add(mode);
        var message = Text(original.LoadMode is >= 3 ? "Current device mode: " + OutputLoadModes.Label(original.LoadMode) : "", 12, Muted);
        panel.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-output-mode" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var editor = EditorWindow("Output mode", panel, 440, 280);
        apply.Click += (_, _) => { try { if (mode.SelectedIndex < 0) throw new ArgumentException("Choose an output mode."); CommitDynamics(new(editedId, channel, LoadMode: mode.SelectedIndex), original); editor.Close(); } catch (Exception ex) { message.Text = ex.Message; } };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, "mode");
    }

    private void OpenBridgeEditor(int channel)
    {
        var pairStart = (channel - 1) / 2 * 2 + 1;
        var editedId = deviceId;
        if (ActivateInputEditor(editedId, pairStart, "bridge"))
            return;
        var original = service.GetOutputDynamics(editedId, pairStart);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(Heading($"Stereo / Bridge · Outputs {(char)('A' + pairStart - 1)}/{(char)('A' + pairStart)}"));
        var choice = new ComboBox { ItemsSource = new[] { "Stereo", "Bridge" }, SelectedIndex = original.Bridged ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "output-bridge-choice" };
        panel.Children.Add(choice);
        panel.Children.Add(Text("Applies to both outputs in this pair.", 12, Muted));
        var message = Text("", 12, Muted);
        panel.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-output-bridge" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var editor = EditorWindow("Output stereo / bridge", panel, 480, 310);
        apply.Click += (_, _) => { try { CommitDynamics(new(editedId, pairStart, Bridged: choice.SelectedIndex == 1), original); editor.Close(); } catch (Exception ex) { message.Text = ex.Message; } };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, pairStart, "bridge");
    }

    private void OpenNoiseGateEditor(int channel)
    {
        var editedId = deviceId;
        if (ActivateInputEditor(editedId, channel, "noise-gate"))
            return;
        var original = service.GetOutputDynamics(editedId, channel);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(Heading($"Noise Gate · Output {(char)('A' + channel - 1)}"));
        var enabled = new CheckBox { Content = "On", IsChecked = original.NoiseGateEnabled, Tag = "noise-gate-enabled" };
        panel.Children.Add(enabled);
        var threshold = Number(-100, 0, 1, original.NoiseGateDb ?? -100);
        threshold.Tag = "noise-gate-db";
        threshold.FormatString = "0";
        threshold.IsEnabled = original.NoiseGateDb is not null;
        panel.Children.Add(Field("Threshold · dB", threshold));
        var message = Text(original.NoiseGateDb is null ? "This native layout does not store a threshold." : "", 12, Muted);
        panel.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-noise-gate" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var editor = EditorWindow("Output noise gate", panel, 440, 350);
        apply.Click += (_, _) => { try { if (threshold.Value is null) return; if (decimal.Truncate(threshold.Value.Value) != threshold.Value.Value) throw new ArgumentException("Enter a whole dB value."); CommitDynamics(new(editedId, channel, NoiseGateEnabled: enabled.IsChecked == true, NoiseGateDb: original.NoiseGateDb is null ? null : checked((int)threshold.Value.Value)), original); editor.Close(); } catch (Exception ex) { message.Text = ex.Message; } };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, "noise-gate");
    }
    private void OpenLimiterEditor(int channel)
    {
        var editedId = deviceId;
        if (ActivateInputEditor(editedId, channel, "limiters"))
            return;
        var original = service.GetOutputDynamics(editedId, channel);
        var rms = original.Rms;
        var peak = original.Peak;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        panel.Children.Add(Heading($"Limiters · Output {(char)('A' + channel - 1)}"));
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        var left = new StackPanel { Spacing = 12 };
        var right = new StackPanel { Spacing = 12 };
        AddColumn(columns, Card(left), 0);
        AddColumn(columns, Card(right), 1);
        panel.Children.Add(columns);
        var rmsEnabled = new CheckBox { Content = "RMS On", IsChecked = rms.Enabled, Tag = "rms-enabled" };
        left.Children.Add(rmsEnabled);
        NumericUpDown Input(StackPanel parent, string label, string tag, decimal min, decimal max, decimal step, double value)
        {
            var input = Number(min, max, step, (decimal)value);
            input.Tag = tag;
            input.FormatString = step == 1 ? "0" : "0.00";
            parent.Children.Add(Field(label, input));
            return input;
        }
        var rmsVolts = Input(left, "Threshold · Vrms", "rms-threshold", 0, (decimal)rms.MaximumVolts, .1m, rms.ThresholdVolts);
        left.Children.Add(Text($"Device maximum · {rms.MaximumVolts:0.00} Vrms", 11, Muted));
        var attack = Input(left, "Attack · ms", "rms-attack", 1, 2000, 1, rms.AttackMs);
        var release = Input(left, "Release · × Attack", "rms-release", 2, 32, 1, rms.ReleaseMultiplier);
        var auto = new CheckBox { Content = "Automatic timing", IsChecked = rms.Automatic, Tag = "rms-auto" };
        left.Children.Add(auto);
        void Timing()
        {
            attack.IsEnabled = release.IsEnabled = auto.IsChecked != true;
        }
        auto.IsCheckedChanged += (_, _) => Timing();
        Timing();
        var peakEnabled = new CheckBox { Content = "Peak On", IsChecked = peak.Enabled, Tag = "peak-enabled" };
        right.Children.Add(peakEnabled);
        var peakVolts = Input(right, "Threshold · Vpeak", "peak-threshold", 0, (decimal)peak.MaximumVolts, .1m, peak.ThresholdVolts);
        right.Children.Add(Text($"Device maximum · {peak.MaximumVolts:0.00} Vpeak", 11, Muted));
        var hold = Input(right, "Hold · ms", "peak-hold", 0, 2000, 1, peak.HoldMs);
        var decay = Input(right, "Release · ms", "peak-release", 20, 1000, 1, peak.ReleaseMs);
        var message = Text("", 12, Muted);
        panel.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-limiters" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var editor = EditorWindow("Output limiters", panel, 760, 590);
        editor.MinWidth = 720;
        editor.MinHeight = 570;
        apply.Click += (_, _) =>
        {
            try
            {
                if (new[] { rmsVolts, attack, release, peakVolts, hold, decay }.Any(n => n.Value is null))
                    return;
                if (new[] { attack, release, hold, decay }.Any(n => decimal.Truncate(n.Value!.Value) != n.Value.Value))
                    throw new ArgumentException("Enter whole timing values.");
                var nextRms = rms with
                {
                    Enabled = rmsEnabled.IsChecked == true,
                    ThresholdVolts = (double)rmsVolts.Value!.Value,
                    AttackMs = checked((int)attack.Value!.Value),
                    ReleaseMultiplier = checked((int)release.Value!.Value),
                    Automatic = auto.IsChecked == true
                };
                var nextPeak = peak with
                {
                    Enabled = peakEnabled.IsChecked == true,
                    ThresholdVolts = (double)peakVolts.Value!.Value,
                    HoldMs = checked((int)hold.Value!.Value),
                    ReleaseMs = checked((int)decay.Value!.Value)
                };
                CommitDynamics(new(editedId, channel, Rms: nextRms, Peak: nextPeak), original);
                editor.Close();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, "limiters");
    }
}
