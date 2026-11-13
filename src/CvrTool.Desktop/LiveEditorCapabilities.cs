// Desktop / Verified live editor controls
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: permit only captured command variants while retaining protected viewers.
// Units: native dB/ms/Hz/Q fields and active-low bypass controls.
// State: capability plus a fresh connection is required; unsupported controls stay disabled.
// Scope: no reset, bridge, 100V, limiter edits, output EQ, unverified crossover types or FIR coefficient writes.
using Avalonia.Controls;
using CvrTool.Application;
using Avalonia.LogicalTree;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private void ConfigureLiveEditor(Window editor, string device, int channel, string kind)
    {
        DisableLiveEdits(editor, true);
        var snapshot = service.GetDevice(device);
        var allowed = ConnectionAvailable(snapshot) && snapshot.Readings?.CanSetVerifiedDsp == true &&
            kind is "delay" or "output-delay" or "output-eq" or "eq" or "mode" or "noise-gate" or "fir" or "limiters";
        editor.Title += allowed ? " · Live" : " · Read-only";
        if (!allowed) return;
        var input = kind == "eq" ? service.GetInputProcessing(device, channel) : null;
        var snapshotOutput = kind == "output-eq" ? service.GetOutputProcessing(device, channel) : null;
        foreach (var control in editor.GetLogicalDescendants().OfType<Control>())
        {
            var tag = control.Tag?.ToString() ?? "";
            if (control is TextBlock help && tag == "eq-help" && kind == "eq")
                help.Text = "Drag Peak bands to adjust Hz/dB · BW-12/BW-24 HP/LP are editable; other filter types stay protected · 48 kHz preview";
            if (control is EqualizerGraph graph && kind == "eq")
            {
                graph.CanEditCrossovers = ObservedInputCrossovers.CanWrite(input!.HighPass) && ObservedInputCrossovers.CanWrite(input.LowPass);
                graph.PeakBandsOnly = true;
                graph.IsEnabled = true;
            }
            if (kind == "output-eq" && control is EqualizerGraph outputGraph)
            {
                outputGraph.IsEnabled = true;
                outputGraph.PeakBandsOnly = true;
            }
            var enable = kind switch
            {
                "delay" or "output-delay" => tag is "delay-ms" or "delay-meters" or "apply-delay",
                "mode" => tag is "output-mode-choice" or "apply-output-mode",
                "noise-gate" => tag is "noise-gate-enabled" or "apply-noise-gate" ||
                    (tag == "noise-gate-db" && service.GetOutputDynamics(device, channel).NoiseGateDb is not null),
                "limiters" => tag.StartsWith("limiter-") || tag == "apply-limiters" || tag.StartsWith("peak-") ||
                    (tag.StartsWith("rms-") && (tag is not ("rms-attack" or "rms-release") ||
                    !service.GetOutputDynamics(device, channel).Rms.Automatic)),
                "fir" => tag is "fir-enabled" or "apply-fir" or "fir-import" or "fir-remove",
                "output-eq" => tag is "input-eq-enabled" or "apply-input-eq" ||
                    tag.StartsWith("hp-") || tag.StartsWith("lp-") ||
                    (tag.StartsWith("eq-") && int.TryParse(tag.Split('-').Last(), out var number) &&
                        snapshotOutput!.Bands.Any(b => b.Number == number && b.FilterType == 0)),
                "eq" => tag is "input-eq-enabled" or "apply-input-eq" ||
                    (tag is "hp-type" or "hp-frequency" or "hp-enabled" && ObservedInputCrossovers.CanWrite(input!.HighPass)) ||
                    (tag is "lp-type" or "lp-frequency" or "lp-enabled" && ObservedInputCrossovers.CanWrite(input!.LowPass)) ||
                    (tag.StartsWith("eq-") && int.TryParse(tag.Split('-').Last(), out var band) &&
                        input!.Bands.Any(b => b.Number == band && b.FilterType == 0)),
                _ => false
            };
            if (enable || tag is "editor-copy" or "eq-reset") control.IsEnabled = true;
        }
    }
}
