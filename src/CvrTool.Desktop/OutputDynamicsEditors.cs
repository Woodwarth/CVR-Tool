// Desktop / Output dynamics editors
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
    private async Task CommitDynamics(OutputDynamicsChange change, OutputDynamicsState original)
    {
        void Apply()
        {
            var plan = service.PlanOutputDynamics(change);
            if (plan.Before != original) throw new InvalidOperationException("Output dynamics changed externally. Reopen this editor.");
            service.ApplyOutputDynamics(plan.Id);
        }
        if (liveReadOnly) await Task.Run(Apply); else Apply();
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
        var choices = service.GetDevice(editedId).Readings?.ReadOnly == true ? OutputLoadModes.Options.Take(2).ToArray() : OutputLoadModes.Options;
        var mode = new ComboBox { ItemsSource = choices, SelectedIndex = original.LoadMode is { } selectedMode && selectedMode >= 0 && selectedMode < choices.Count ? selectedMode : -1, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "output-mode-choice" };
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
        apply.Click += async (_, _) => { try { if (mode.SelectedIndex < 0) throw new ArgumentException("Choose an output mode."); await CommitDynamics(new(editedId, channel, LoadMode: mode.SelectedIndex), original); editor.Close(); } catch (Exception ex) { message.Text = ex.Message; } };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, "mode");
    }

    private readonly HashSet<(string Device, int Pair)> bridgeConfirmations = new();
    private async void ToggleBridge(int channel)
    {
        var editedId = deviceId;
        var pairStart = (channel - 1) / 2 * 2 + 1;
        if (!bridgeConfirmations.Add((editedId, pairStart))) return;
        try
        {
            var current = service.GetOutputDynamics(editedId, pairStart);
            var target = current.Bridged ? "Stereo" : "Bridge";
            if (!await ConfirmPresetAction("Switch to " + target, $"Switch outputs {(char)('A' + pairStart - 1)}/{(char)('A' + pairStart)} to {target}?")) return;
            await CommitDynamics(new(editedId, pairStart, Bridged: !current.Bridged), current);
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { bridgeConfirmations.Remove((editedId, pairStart)); }
    }

    // A bridge follower cannot retain an editable output window from stereo mode.
    private void CloseBridgeFollowerEditors(int channel)
    {
        foreach (var entry in inputEditors.Where(e => e.Key.Device == deviceId && e.Key.Channel == channel &&
            e.Key.Kind is "output-eq" or "output-delay" or "fir" or "mode" or "noise-gate" or "limiters").ToArray())
            entry.Value.Close();
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
        apply.Click += async (_, _) => { try { if (threshold.Value is null) return; if (decimal.Truncate(threshold.Value.Value) != threshold.Value.Value) throw new ArgumentException("Enter a whole dB value."); await CommitDynamics(new(editedId, channel, NoiseGateEnabled: enabled.IsChecked == true, NoiseGateDb: original.NoiseGateDb is null ? null : checked((int)threshold.Value.Value)), original); editor.Close(); } catch (Exception ex) { message.Text = ex.Message; } };
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
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(Heading($"Limiters · Output {(char)('A' + channel - 1)}"));
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        var left = new StackPanel { Spacing = 8 };
        var right = new StackPanel { Spacing = 8 };
        AddColumn(columns, Card(left), 0);
        AddColumn(columns, Card(right), 1);
        panel.Children.Add(columns);
        var rmsEnabled = new CheckBox { Content = "RMS On", IsChecked = rms.Enabled, Tag = "rms-enabled" };

        Grid ParameterRow(string label, Control control)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110"), ColumnSpacing = 6 };
            AddColumn(row,Text(label,12,Muted),0);
            AddColumn(row,control,1);
            return row;
        }
        NumericUpDown Input(StackPanel parent, string label, string tag, decimal min, decimal max, decimal step, double value)
        {
            var input = Number(min, max, step, (decimal)value);
            input.Tag = tag;
            input.FormatString = step == 1 ? "0" : "0.00";
            parent.Children.Add(ParameterRow(label, input));
            return input;
        }
        var rmsVolts = Input(left, "Threshold · Vrms", "rms-threshold", 0, (decimal)rms.MaximumVolts, .1m, rms.ThresholdVolts);

        var attack = Input(left, "Attack · ms", "rms-attack", 1, 2000, 1, rms.AttackMs);
        var release = Input(left, "Release · × Attack", "rms-release", 2, 32, 1, rms.ReleaseMultiplier);
        var auto = new CheckBox { Content = "Automatic timing", IsChecked = rms.Automatic, Tag = "rms-auto" };
        left.Children.Insert(1,auto);
        void Timing()
        {
            attack.IsEnabled = release.IsEnabled = auto.IsChecked != true;
        }
        auto.IsCheckedChanged += (_, _) => Timing();
        Timing();
        var peakEnabled = new CheckBox { Content = "Peak On", IsChecked = peak.Enabled, Tag = "peak-enabled" };

        var peakVolts = Input(right, "Threshold · Vpeak", "peak-threshold", 0, (decimal)peak.MaximumVolts, .1m, peak.ThresholdVolts);

        var hold = Input(right, "Hold · ms", "peak-hold", 0, 2000, 1, peak.HoldMs);
        var decay = Input(right, "Release · ms", "peak-release", 20, 1000, 1, peak.ReleaseMs);
        var loadOhms = service.GetNativePresetMetadata(editedId)?.Channels.SingleOrDefault(c => c.Channel == channel)?.ConfiguredLoadOhms;
        var load = Number(.1m, 10000000, .1m, (decimal)(loadOhms is >= .1 and <= 10000000 ? loadOhms.Value : 8));
        load.Tag = "limiter-load"; load.ShowButtonSpinner = true;
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 20 };
        var center = new StackPanel { Spacing = 6 };
        var meters = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
        center.Children.Add(meters); AddColumn(top,center,1);
        panel.Children.Insert(1,top);
        Slider Fader(string label, NumericUpDown volts)
        {
            var slider = new Slider { Minimum = 0, Maximum = (double)volts.Maximum,
                Value = (double)(volts.Value ?? 0), Height = 150, Width = 34, Orientation = Orientation.Vertical,
                Tag = "limiter-fader-" + label };
            var strip = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            strip.Children.Add(Centered(label,14,Accent)); strip.Children.Add(slider);
            strip.Children.Add(label == "RMS" ? rmsEnabled : peakEnabled);
            AddColumn(top,strip,label == "RMS" ? 0 : 2);
            bool syncing = false;
            slider.ValueChanged += (_,_) =>
            {
                if (syncing) return;
                syncing = true; volts.Value = (decimal)Math.Round(slider.Value,2); syncing = false;
            };
            volts.ValueChanged += (_,_) =>
            {
                if (syncing || volts.Value is null) return;
                syncing = true; slider.Value = (double)volts.Value.Value; syncing = false;
            };
            return slider;
        }
        Fader("RMS",rmsVolts);
        var levelMeter = new ChannelMeter(null,-40) { Height = 119, Width = 24 };
        var limitMeter = new ChannelMeter(null,-40) { Height = 119, Width = 24 };
        var levelText = Text("—",12,Muted); var limitText = Text("—",12,Muted);
        foreach (var (label,meter,reading) in new[]{ ("Level",levelMeter,levelText), ("Limit",limitMeter,limitText) })
        {
            var column = new StackPanel { Spacing = 2 };
            column.Children.Add(meter); column.Children.Add(reading); meters.Children.Add(Field(label,column));
        }
        Fader("Peak",peakVolts);
        load.Width = 95; center.Children.Add(Field("Load · Ω",load));
        void PowerField(StackPanel column,string label,string tag,NumericUpDown volts)
        {
            var watts = Number(0,decimal.MaxValue,1,0);
            watts.FormatString="0"; watts.ShowButtonSpinner=true; watts.Tag=tag;
            column.Children.Insert(1,ParameterRow(label,watts));
            bool syncing=false;
            void FromVolts()
            {
                if (syncing || load.Value is not > 0 || volts.Value is null) return;
                syncing=true;
                watts.Maximum = decimal.Ceiling(volts.Maximum * volts.Maximum / load.Value.Value);
                watts.Value = Math.Round(volts.Value.Value * volts.Value.Value / load.Value.Value);
                syncing=false;
            }
            watts.ValueChanged += (_,_) =>
            {
                if(syncing || watts.Value is null || load.Value is not > 0) return;
                volts.Value=Math.Min(volts.Maximum,(decimal)Math.Round(Math.Sqrt((double)watts.Value.Value * (double)load.Value.Value),2));
            };
            volts.ValueChanged += (_,_)=>FromVolts();
            load.ValueChanged += (_,_)=>FromVolts();
            FromVolts();
        }
        PowerField(left,"Prms · W","rms-power",rmsVolts);
        PowerField(right,"Ppeak · W","peak-power",peakVolts);
        var message = Text("", 12, Muted);
        panel.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-limiters" };
        var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        var editor = EditorWindow("Output limiters", new ScrollViewer { Content = panel }, 700, 650);
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void RefreshMeters()
        {
            try
            {
                var telemetry = service.GetTelemetry(editedId).Single(t => t.Channel == channel);
                var db = telemetry.OutputMeterDb ?? telemetry.LevelDbFs;
                levelMeter.Update(db); limitMeter.Update(telemetry.LimitDb);
                levelText.Text = KnownDb(db); limitText.Text = KnownDb(telemetry.LimitDb);
            }
            catch (Exception) { levelMeter.Update(null); limitMeter.Update(null); levelText.Text = limitText.Text = "—"; }
        }
        static string KnownDb(double? db) => db is null ? "—" : $"{db:0.#} dB";
        timer.Tick += (_,_)=>RefreshMeters();
        editor.Opened += (_,_)=>{ RefreshMeters(); timer.Start(); };
        editor.Closed += (_,_)=>timer.Stop();
        editor.MinWidth = 640;
        editor.MinHeight = 570;
        (RmsLimiterState Rms, PeakLimiterState Peak) Draft()
        {
            if (new[] { rmsVolts, attack, release, peakVolts, hold, decay }.Any(n => n.Value is null))
                throw new ArgumentException("Enter all limiter settings.");
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
            return (nextRms,nextPeak);
        }
        var initialDraft = Draft();
        AddChannelNavigation(actions, editor, editedId, channel, true, OpenLimiterEditor,
            () => Draft() != initialDraft, async target =>
            {
                var values = Draft();
                var dest = service.GetOutputDynamics(editedId,target);
                await CommitDynamics(new(editedId,target,Rms:values.Rms with { MaximumVolts=dest.Rms.MaximumVolts },
                    Peak:values.Peak with { MaximumVolts=dest.Peak.MaximumVolts }),dest);
            },message);
        apply.Click += async (_, _) =>
        {
            try
            {
                var (nextRms,nextPeak) = Draft();
                await CommitDynamics(new(editedId, channel, Rms: nextRms, Peak: nextPeak), original);
                editor.Close();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, "limiters");
    }
}
