// Desktop / Output FIR editor
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: import/export/remove FIR drafts and display the reference's three response plots.
// Units: 512 taps maximum, 48 kHz, milliseconds, dB and wrapped phase degrees.
// State: apply uses the shared output FIR backend; one editor per channel.
// Scope: graph calculations preview coefficients, not measured amplifier behavior.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private void OpenFirEditor(int channel)
    {
        var editedId = deviceId;
        if (ActivateInputEditor(editedId, channel, "fir"))
            return;
        var original = service.GetFir(editedId, channel);
        var draft = original with
        {
            Coefficients = original.Coefficients.ToArray()
        };
        var content = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Heading("Output FIR · Channel " + channel));
        var enabled = new CheckBox { Content = draft.Enabled ? "FIR On" : "FIR Off", IsChecked = draft.Enabled, Tag = "fir-enabled" };
        AddColumn(header, enabled, 1);
        content.Children.Add(header);
        var info = Text("", 13, Muted);
        info.Tag = "fir-info";
        content.Children.Add(info);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var import = new Button { Content = "Import", Tag = "fir-import" };
        var export = new Button { Content = "Export", Tag = "fir-export" };
        var remove = new Button { Content = "Remove", Tag = "fir-remove" };
        actions.Children.Add(import);
        actions.Children.Add(export);
        actions.Children.Add(remove);
        content.Children.Add(actions);
        var impulse = new ResponsePlot { Height = 110, Minimum = -1, Maximum = 1, Tag = "fir-impulse" };
        var magnitude = new ResponsePlot { Height = 110, Minimum = -24, Maximum = 24, Tag = "fir-magnitude", Logarithmic = true };
        var phase = new ResponsePlot { Height = 110, Minimum = -180, Maximum = 180, Tag = "fir-phase", Logarithmic = true };
        void AddPlot(string label, ResponsePlot plot)
        {
            content.Children.Add(Text(label, 13));
            content.Children.Add(plot);
            var ticks = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
            var labels = plot.Logarithmic ? new[] { "20 Hz", "118 Hz", "693 Hz", "4.08 kHz", "24 kHz" } : new[] { "0 ms", "2.66 ms", "5.32 ms", "7.98 ms", "10.65 ms" };
            for (var n = 0; n < 5; n++)
                AddColumn(ticks, Text(labels[n], 10, Muted), n);
            content.Children.Add(ticks);
        }
        AddPlot("Impulse Response · ms", impulse);
        AddPlot("Frequency Response · Hz / dB", magnitude);
        AddPlot("Phase Response · Hz / degrees", phase);
        var message = Text("Import one coefficient per line. Apply commits this output's FIR settings. Curves preview coefficients.", 11, Muted);
        content.Children.Add(message);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-fir" };
        var cancel = new Button { Content = "Cancel" };
        footer.Children.Add(apply);
        footer.Children.Add(cancel);
        content.Children.Add(footer);
        var canvas = new Viewbox { Child = new Border { Width = 1100, Height = 720, Child = content }, Stretch = Stretch.Uniform };
        var editor = EditorWindow("Output FIR · Channel " + channel, canvas, 1140, 780);
        editor.MinWidth = 850;
        editor.MinHeight = 570;
        void Refresh()
        {
            if (!draft.CoefficientsKnown || draft.Coefficients.Count == 0)
            {
                info.Text = "FIR coefficients unknown · Not included in the received settings or preset";
                impulse.Points = []; magnitude.Points = []; phase.Points = [];
                export.IsEnabled = false; apply.IsEnabled = false;
                return;
            }
            export.IsEnabled = true; apply.IsEnabled = true;
            var text = string.Join("\n", draft.Coefficients.Select(c => c.ToString("R", CultureInfo.InvariantCulture)));
            var analysis = FirFileAnalyzer.Analyze(text);
            var count = draft.Coefficients.Select((c, n) => (c, n)).Where(x => x.c != 0).Select(x => x.n + 1).DefaultIfEmpty(0).Max();
            var peak = draft.Coefficients.Select((c, n) => (Value: Math.Abs(c), Index: n)).MaxBy(x => x.Value).Index;
            info.Text = $"Name: {(string.IsNullOrEmpty(draft.Name) ? "—" : draft.Name)}    Rate: 48 kHz    Taps: {count} / 512    Peak delay: {peak / 48.0:0.000} ms";
            var amplitude = Math.Max(1, draft.Coefficients.Max(Math.Abs) * 1.1);
            impulse.Minimum = -amplitude;
            impulse.Maximum = amplitude;
            impulse.Points = Enumerable.Range(0, 512).Select(n => new ResponsePoint(n / 48.0, n < draft.Coefficients.Count ? draft.Coefficients[n] : 0)).ToArray();
            magnitude.Points = analysis.Response.Select(p => new ResponsePoint(p.FrequencyHz, p.MagnitudeDb)).ToArray();
            phase.Points = analysis.Response.Select(p => new ResponsePoint(p.FrequencyHz, p.PhaseDegrees)).ToArray();
        }
        enabled.IsCheckedChanged += (_, _) => { draft = draft with { Enabled = enabled.IsChecked == true }; enabled.Content = draft.Enabled ? "FIR On" : "FIR Off"; };
        import.Click += async (_, _) =>
        {
            try
            {
                var files = await editor.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import FIR coefficients", AllowMultiple = false, FileTypeFilter = [new("FIR text") { Patterns = ["*.txt"] }] });
                if (files.Count != 1)
                    return;
                await using var stream = await files[0].OpenReadAsync();
                var analysis = await FirFileAnalyzer.AnalyzeAsync(stream);
                var name = Path.GetFileNameWithoutExtension(files[0].Name);
                if (name.Length > 32)
                    name = name[..32];
                draft = CvrTool.Application.AmplifierService.ValidateFir(new(channel, name, analysis.Coefficients, draft.Enabled));
                Refresh();
                message.Text = "FIR imported · Apply to commit.";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        export.Click += async (_, _) =>
        {
            try
            {
                var file = await editor.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export FIR coefficients", SuggestedFileName = (string.IsNullOrEmpty(draft.Name) ? "FIR" : draft.Name) + ".txt", DefaultExtension = "txt" });
                if (file is null)
                    return;
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(string.Join("\n", draft.Coefficients.Select(c => c.ToString("R", CultureInfo.InvariantCulture))));
                message.Text = "FIR exported.";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        remove.Click += (_, _) => { draft = new(channel, "", new double[] { 1 }, false); enabled.IsChecked = false; Refresh(); message.Text = "FIR removed from draft · Apply to commit."; };
        apply.Click += async (_, _) =>
        {
            try
            {
                var latest = service.GetFir(editedId, channel);
                if (latest.Enabled != original.Enabled || latest.Name != original.Name || !latest.Coefficients.SequenceEqual(original.Coefficients))
                    throw new InvalidOperationException("Output FIR changed externally. Reopen this editor.");
                if (service.GetDevice(editedId).Readings?.ReadOnly == true) await Task.Run(() => service.SetFirEnabled(editedId,channel,draft.Enabled));
                else service.SetFir(editedId, draft);
                Render();
                editor.Close();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        Refresh();
        ShowInputEditor(editor, editedId, channel, "fir");
    }
}

public readonly record struct ResponsePoint(double X, double Y);
public sealed class ResponsePlot : Control
{
    private IReadOnlyList<ResponsePoint> points = [];
    public IReadOnlyList<ResponsePoint> Points
    {
        get => points; set
        {
            points = value.ToArray();
            InvalidateVisual();
        }
    }
    public double Minimum { get; set; } = -24; public double Maximum { get; set; } = 24; public bool Logarithmic
    {
        get; init;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(Brush.Parse("#101B23"), new Pen(Brush.Parse("#46525D"), 1), rect);
        for (var n = 1; n < 10; n++)
            context.DrawLine(new Pen(Brush.Parse("#34454F"), 1), new(Bounds.Width * n / 10, 0), new(Bounds.Width * n / 10, Bounds.Height));
        for (var n = 1; n < 6; n++)
            context.DrawLine(new Pen(Brush.Parse("#34454F"), 1), new(0, Bounds.Height * n / 6), new(Bounds.Width, Bounds.Height * n / 6));
        if (points.Count == 0)
            return;
        var first = points[0].X;
        var last = Math.Max(first + .001, points[^1].X);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            var started = false;
            foreach (var point in points)
            {
                if (double.IsNaN(point.Y))
                {
                    if (started)
                        drawing.EndFigure(false);
                    started = false;
                    continue;
                }
                var ratio = Logarithmic ? Math.Log(Math.Max(20, point.X) / 20) / Math.Log(Math.Max(21, last) / 20) : (point.X - first) / (last - first);
                var mapped = new Point(Math.Clamp(ratio, 0, 1) * Bounds.Width, (Maximum - Math.Clamp(point.Y, Minimum, Maximum)) / (Maximum - Minimum) * Bounds.Height);
                if (!started)
                {
                    drawing.BeginFigure(mapped, false);
                    started = true;
                }
                else
                    drawing.LineTo(mapped);
            }
            if (started)
                drawing.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brush.Parse("#00CEBD"), 1.5), geometry);
    }
}
