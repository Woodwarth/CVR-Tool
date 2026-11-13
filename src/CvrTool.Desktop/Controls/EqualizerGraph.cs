// Desktop / Graphical input EQ
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: plot the summed response and edit numbered band handles.
// Units: logarithmic 20 Hz–20 kHz axis, ±24 dB view, 48 kHz preview.
// State: dragging and keyboard edits update the editor draft only.
// Scope: unsupported filter types show handles without an invented curve.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed class EqualizerGraph : Control
{
    private IReadOnlyList<EqualizerBand> bands = [];
    private bool enabled = true;
    private CrossoverFilter? highPass, lowPass;
    public bool CanEditCrossovers { get; set; } = true;
    public bool PeakBandsOnly { get; set; }
    public event Action<bool, CrossoverFilter>? CrossoverChanged;
    public CrossoverFilter? HighPass
    {
        get => highPass; set
        {
            highPass = value;
            InvalidateVisual();
        }
    }
    public CrossoverFilter? LowPass
    {
        get => lowPass; set
        {
            lowPass = value;
            InvalidateVisual();
        }
    }
    private int selected = 1;
    private bool dragging;
    public event Action<EqualizerBand>? BandChanged;
    public IReadOnlyList<EqualizerBand> Bands
    {
        get => bands; set
        {
            bands = value.ToArray();
            InvalidateVisual();
        }
    }
    public bool EqEnabled
    {
        get => enabled; set
        {
            enabled = value;
            InvalidateVisual();
        }
    }
    private Rect Plot => new(46, 12, Math.Max(1, Bounds.Width - 96), Math.Max(1, Bounds.Height - 45));
    public EqualizerGraph()
    {
        Height = 330;
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }
    public Point BandPosition(EqualizerBand band) => new(Plot.X + Math.Log(band.FrequencyHz / 20) / Math.Log(1000) * Plot.Width, Plot.Y + (24 - band.GainDb) / 48 * Plot.Height);
    private static FormattedText Label(string text, double size, IBrush brush) => new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI, sans-serif"), size, brush);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var plot = Plot;
        context.DrawRectangle(Brush.Parse("#101B23"), null, plot);
        var grid = new Pen(Brush.Parse("#34454F"), 1);
        foreach (var frequency in new[] { 20, 30, 40, 50, 60, 80, 100, 200, 300, 400, 500, 600, 800, 1000, 2000, 3000, 4000, 5000, 6000, 8000, 10000, 20000 })
        {
            var x = plot.X + Math.Log(frequency / 20.0) / Math.Log(1000) * plot.Width;
            context.DrawLine(grid, new(x, plot.Top), new(x, plot.Bottom));
            if (frequency is 20 or 50 or 100 or 200 or 500 or 1000 or 2000 or 5000 or 10000 or 20000)
            {
                var text = Label(frequency >= 1000 ? frequency / 1000 + "k" : frequency.ToString(), 11, Brushes.LightGray);
                context.DrawText(text, new(x - text.Width / 2, plot.Bottom + 8));
            }
        }
        for (var db = -24; db <= 24; db += 6)
        {
            var y = plot.Y + (24 - db) / 48.0 * plot.Height;
            context.DrawLine(new Pen(Brush.Parse(db == 0 ? "#65828C" : "#34454F"), 1), new(plot.Left, y), new(plot.Right, y));
            var text = Label(db.ToString(), 11, Brushes.LightGray);
            context.DrawText(Label($"{db * 7.5:0}°", 11, Brush.Parse("#F0BD71")), new(plot.Right + 6, y - 7));
            context.DrawText(text, new(Plot.X - text.Width - 9, y - 7));
        }
        var available = bands.All(b => !b.Enabled || b.FilterType == 0) && new[] { highPass, lowPass }.All(f => f is null || !f.Enabled || CrossoverFilters.Options.Any(o => o.Code == f.FilterType));
        if (available)
        {
            var geometry = new StreamGeometry();
            var phaseGeometry = new StreamGeometry();
            using (var phase = phaseGeometry.Open())
            using (var path = geometry.Open())
            {
                double previous = 0;
                for (var pixel = 0; pixel <= 500; pixel++)
                {
                    var frequency = 20 * Math.Pow(1000, pixel / 500.0);
                    var response = System.Numerics.Complex.One;
                    if (enabled)
                    {
                        response = CrossoverResponse.Transfer(highPass, true, frequency) * CrossoverResponse.Transfer(lowPass, false, frequency);
                        foreach (var band in bands)
                            response *= EqualizerResponse.Transfer(band, frequency);
                    }
                    var db = 20 * Math.Log10(response.Magnitude);
                    double degrees = response.Phase * 180 / Math.PI;
                    double x = plot.X + pixel / 500.0 * plot.Width;
                    var phasePoint = new Point(x, plot.Y + (180 - degrees) / 360 * plot.Height);
                    bool wrap = pixel > 0 && Math.Abs(previous - degrees) > 180;
                    if (wrap) phase.EndFigure(false);
                    if (pixel == 0 || wrap) phase.BeginFigure(phasePoint, false);
                    else phase.LineTo(phasePoint);
                    previous = degrees;
                    var point = new Point(x, plot.Y + (24 - Math.Clamp(db, -24, 24)) / 48 * plot.Height);
                    if (pixel == 0)
                        path.BeginFigure(point, false);
                    else
                        path.LineTo(point);
                }
                phase.EndFigure(false);
                path.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(Brush.Parse(enabled ? "#00CEBD" : "#71838E"), 2), geometry);
            context.DrawGeometry(null, new Pen(Brush.Parse("#F0BD71"), 1.5, DashStyle.Dash), phaseGeometry);
        }
        else
            context.DrawText(Label("Curve unavailable for device filter type", 12, Brushes.LightGray), new(plot.Left + 12, plot.Top + 10));
        foreach (var band in bands)
        {
            var point = BandPosition(band);
            var active = band.Enabled && enabled;
            context.DrawEllipse(Brush.Parse(band.Number == selected ? "#007F7D" : "#101B23"), new Pen(active ? Brushes.White : Brushes.Gray, 2), point, 13, 13);
            var text = Label(band.Number.ToString(), 12, active ? Brushes.White : Brushes.Gray);
            context.DrawText(text, new(point.X - text.Width / 2, point.Y - text.Height / 2));
        }
        foreach (var (filter, label, index) in new[] { (highPass, "HP", 0), (lowPass, "LP", 9) })
        {
            if (filter is null)
                continue;
            var point = CrossoverPosition(filter);
            var color = Brush.Parse(filter.Enabled && enabled ? "#F0BD71" : "#71838E");
            context.DrawEllipse(Brush.Parse(selected == index ? "#715322" : "#101B23"), new Pen(color, 2), point, 15, 15);
            var text = Label(label, 10, color);
            context.DrawText(text, new(point.X - text.Width / 2, point.Y - text.Height / 2));
        }
    }
    private Point CrossoverPosition(CrossoverFilter filter) => new(Plot.X + Math.Log(Math.Clamp(filter.FrequencyHz, 20, 20000) / 20) / Math.Log(1000) * Plot.Width, Plot.Y + Plot.Height / 2);
    public void MoveCrossover(bool high, double frequency)
    {
        var filter = (high ? HighPass : LowPass);
        if (!IsEffectivelyEnabled || !CanEditCrossovers || filter is null)
            return;
        filter = filter with
        {
            FrequencyHz = Math.Round(Math.Clamp(frequency, 20, 20000))
        };
        if (high)
            HighPass = filter;
        else
            LowPass = filter;
        CrossoverChanged?.Invoke(high, filter);
    }
    public void MoveBand(int number, double frequency, double gain)
    {
        var original = bands.Single(b => b.Number == number);
        if (!IsEffectivelyEnabled || (PeakBandsOnly && original.FilterType != 0)) return;
        var band = original with
        {
            FrequencyHz = Math.Round(Math.Clamp(frequency, 20, 20000)),
            GainDb = Math.Round(Math.Clamp(gain, -18, 18), 1)
        };
        Bands = bands.Select(b => b.Number == number ? band : b).ToArray();
        BandChanged?.Invoke(band);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || bands.Count == 0)
            return;
        var point = e.GetPosition(this);
        double Distance(EqualizerBand b)
        {
            var p = BandPosition(b);
            return Math.Sqrt(Math.Pow(p.X - point.X, 2) + Math.Pow(p.Y - point.Y, 2));
        }
        var editableBands = bands.Where(b => !PeakBandsOnly || b.FilterType == 0).ToArray();
        if (editableBands.Length == 0) return;
        var closest = editableBands.MinBy(Distance)!;
        var best = Distance(closest);
        selected = closest.Number;
        foreach (var (filter, index) in new[] { (highPass, 0), (lowPass, 9) })
        {
            if (!CanEditCrossovers || filter is null)
                continue;
            var p = CrossoverPosition(filter);
            var distance = Math.Sqrt(Math.Pow(p.X - point.X, 2) + Math.Pow(p.Y - point.Y, 2));
            if (distance < best)
            {
                best = distance;
                selected = index;
            }
        }
        if (best > 22)
            return;
        dragging = true;
        Focus();
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging)
            return;
        var point = e.GetPosition(this);
        var frequency = 20 * Math.Pow(1000, (point.X - Plot.X) / Plot.Width);
        if (selected is 0 or 9)
            MoveCrossover(selected == 0, frequency);
        else
            MoveBand(selected, frequency, 24 - (point.Y - Plot.Y) / Plot.Height * 48);
        e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        dragging = false;
        e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (selected is 0 or 9)
        {
            var filter = selected == 0 ? highPass : lowPass;
            if (filter is not null && e.Key is Key.Left or Key.Right)
            {
                MoveCrossover(selected == 0, filter.FrequencyHz * (e.Key == Key.Right ? 1.01 : .99));
                e.Handled = true;
            }
            return;
        }
        var band = bands.SingleOrDefault(b => b.Number == selected);
        if (band is null)
            return;
        if (e.Key is Key.Up or Key.Down)
            MoveBand(selected, band.FrequencyHz, band.GainDb + (e.Key == Key.Up ? .5 : -.5));
        else if (e.Key is Key.Left or Key.Right)
            MoveBand(selected, band.FrequencyHz * (e.Key == Key.Right ? 1.01 : .99), band.GainDb);
        else
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
    }
}
