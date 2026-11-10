// Desktop / DSP previews — static EQ reference and read-only FIR file plots.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private FirFileAnalysis? firAnalysis;
    private string? firFileName;
    public void ShowFirFile(string name, FirFileAnalysis analysis)
    {
        firFileName = System.IO.Path.GetFileName(name);
        firAnalysis = analysis;
        activeTab = "FIR";
        status.Text = "FIR file analyzed locally · No device filter enabled";
        Render();
    }

    private Control FirPage()
    {
        var panel = new StackPanel
        {
            Spacing = 8
        };
        panel.Children.Add(Heading("FIR · Local file analysis"));
        var open = new Button
        {
            Content = "Open FIR text file",
            Background = Brush.Parse("#007F7D")
        };
        open.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open FIR coefficients", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("FIR text (one number per line)") { Patterns = ["*.txt"] }] });
                if (files.Count != 1)
                    return;
                await using var stream = await files[0].OpenReadAsync();
                var analysis = await FirFileAnalyzer.AnalyzeAsync(stream);
                ShowFirFile(files[0].Name, analysis);
            }
            catch (Exception ex)
            {
                status.Text = "FIR file analysis failed: " + ex.Message;
            }
        };
        panel.Children.Add(open);
        panel.Children.Add(Text("One number per line · Decimal point · 48 kHz preview. The text file does not contain a sample rate.", 12, Muted));
        if (firAnalysis is not { } analysis)
        {
            panel.Children.Add(Card(Text("Open a FIR text file to calculate its impulse, frequency and phase response locally. No device filter is enabled.", 15, Muted)));
        }
        else
        {
            panel.Children.Add(Text($"{firFileName} · {analysis.Coefficients.Count:N0} coefficients · {analysis.NonZeroCount:N0} nonzero · {analysis.SampleRate:N0} Hz preview", 14, Accent));
            var peak = Math.Max(1, analysis.Coefficients.Max(c => Math.Abs(c)) * 1.1);
            var duration = (analysis.Coefficients.Count - 1) * 1000.0 / analysis.SampleRate;
            var impulse = analysis.Coefficients.Select((value, index) => new PlotPoint(analysis.Coefficients.Count == 1 ? 0 : (double)index / (analysis.Coefficients.Count - 1), value)).ToArray();
            panel.Children.Add(Plot("Impulse response", impulse, -peak, peak, "0 ms", duration.ToString("0.###", CultureInfo.CurrentCulture) + " ms", "Amplitude", compact: true));
            var upper = analysis.Response[^1].FrequencyHz;
            var response = analysis.Response.Select(p => new PlotPoint(Math.Log(p.FrequencyHz / 20) / Math.Log(upper / 20), p.MagnitudeDb)).ToArray();
            panel.Children.Add(Plot("Frequency response · Display −24 to +24 dB", response, -24, 24, "20 Hz", upper.ToString("N0") + " Hz · logarithmic", "dB", compact: true));
            var phase = analysis.Response.Select(p => new PlotPoint(Math.Log(p.FrequencyHz / 20) / Math.Log(upper / 20), p.PhaseDegrees)).ToArray();
            panel.Children.Add(Plot("Phase · Wrapped to ±180°", phase, -180, 180, "20 Hz", upper.ToString("N0") + " Hz · logarithmic", "Degrees", compact: true));
            if (analysis.NonZeroCount == 0)
                panel.Children.Add(Text("Zero filter: no output; phase is undefined.", 12, Muted));
            panel.Children.Add(Text("Calculated preview · No device transfer · Device tap limits and compatibility are not verified.", 11, Muted));
        }

        return new ScrollViewer
        {
            Content = panel
        };
    }

    private static Control EqReferencePage()
    {
        var panel = new StackPanel
        {
            Spacing = 18
        };
        panel.Children.Add(Heading("EQ · DSP-1502 demo reference"));
        panel.Children.Add(Text("Eight peak bands from the reference image. Static reference; not active EQ settings.", 13, Muted));
        var frequencies = new[]
        {
            60,
            132,
            290,
            639,
            1406,
            3092,
            6803,
            14966
        };
        panel.Children.Add(Plot("Reference curve · 0 dB", [new(0, 0), new(1, 0)], -24, 24, "20 Hz", "20,000 Hz · logarithmic", "dB"));
        var bands = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", frequencies.Length))),
            ColumnSpacing = 8
        };
        for (var i = 0; i < frequencies.Length; i++)
        {
            var band = new StackPanel
            {
                Spacing = 12
            };
            band.Children.Add(Text("EQ " + (i + 1), 14, Accent));
            band.Children.Add(Text("Peak", 13));
            band.Children.Add(Text(frequencies[i] + " Hz", 13));
            band.Children.Add(Text("0 dB", 13));
            band.Children.Add(Text("Q 1.41", 12, Muted));
            AddColumn(bands, Card(band), i);
        }

        panel.Children.Add(bands);
        panel.Children.Add(Text("HP / LP and bypass appear in the original dialog. Hardware states, filter types and ranges remain unverified.", 12, Muted));
        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private static Control Plot(string title, IReadOnlyList<PlotPoint> points, double minimum, double maximum, string left, string right, string unit, bool compact = false)
    {
        var content = new StackPanel
        {
            Spacing = compact ? 4 : 8
        };
        content.Children.Add(Text(title, 14, weight: FontWeight.SemiBold));
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("50,*")
        };
        var scale = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*,*")
        };
        scale.Children.Add(Text(maximum.ToString("0.##"), 10, Muted));
        var middle = Text(((maximum + minimum) / 2).ToString("0.##"), 10, Muted);
        Grid.SetRow(middle, 1);
        scale.Children.Add(middle);
        var bottom = Text(minimum.ToString("0.##"), 10, Muted);
        bottom.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(bottom, 2);
        scale.Children.Add(bottom);
        grid.Children.Add(scale);
        AddColumn(grid, new DspResponsePlot(points, minimum, maximum) { Height = compact ? 84 : 128 }, 1);
        content.Children.Add(grid);
        var axis = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        axis.Children.Add(Text(unit + " · " + left, 10, Muted));
        AddColumn(axis, Text(right, 10, Muted), 1);
        content.Children.Add(axis);
        var card = Card(content);
        if (compact)
            card.Padding = new Thickness(10);
        return card;
    }
}

internal readonly record struct PlotPoint(double X, double Y);
internal sealed class DspResponsePlot(IReadOnlyList<PlotPoint> points, double minimum, double maximum) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Math.Max(1, Bounds.Width - 2);
        var height = Math.Max(1, Bounds.Height - 2);
        context.DrawRectangle(Brush.Parse("#141E26"), null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        var gridPen = new Pen(Brush.Parse("#34454F"), 1);
        for (var i = 0; i <= 10; i++)
            context.DrawLine(gridPen, new Point(i * width / 10, 0), new Point(i * width / 10, height));
        for (var i = 0; i <= 4; i++)
            context.DrawLine(gridPen, new Point(0, i * height / 4), new Point(width, i * height / 4));
        if (points.Count == 0)
            return;
        Point Map(PlotPoint p) => new(1 + Math.Clamp(p.X, 0, 1) * width, 1 + (maximum - Math.Clamp(p.Y, minimum, maximum)) / (maximum - minimum) * height);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            var drawing = false;
            foreach (var point in points)
            {
                if (double.IsNaN(point.Y) || !double.IsFinite(point.X))
                {
                    if (drawing)
                        path.EndFigure(false);
                    drawing = false;
                    continue;
                }

                if (drawing)
                    path.LineTo(Map(point));
                else
                {
                    path.BeginFigure(Map(point), false);
                    drawing = true;
                }
            }

            if (drawing)
                path.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(Brush.Parse("#00CEBD"), 2), geometry);
        if (points.Count == 1 && double.IsFinite(points[0].Y))
            context.DrawEllipse(Brush.Parse("#00CEBD"), null, Map(points[0]), 3, 3);
    }
}
