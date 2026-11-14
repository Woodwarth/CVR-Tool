// Desktop / Meter — segmented dBFS display; missing data never lights a segment.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class ChannelMeter(double? level, double floor = -60) : Control
{
    private static readonly IBrush Signal = Brush.Parse("#38FFE0");
    private static readonly IBrush Hot = Brush.Parse("#FF7486");
    private static readonly IBrush SignalHalo = Brush.Parse("#4000CEBD");
    private static readonly IBrush HotHalo = Brush.Parse("#48FF5264");
    private static readonly IBrush Idle = Brush.Parse("#20383E");

    public double? Level { get; private set; } = level;
    public void Update(double? next) { if (Level != next) { Level = next; InvalidateVisual(); } }
    public override void Render(DrawingContext context)
    {
        var width = Math.Min(22, Math.Max(10, Bounds.Width - 10));
        var x = (Bounds.Width - width) / 2;
        var height = Math.Max(10, Bounds.Height - 4);
        context.DrawRectangle(Brush.Parse("#0D161D"), new Pen(Brush.Parse("#53646F")), new Rect(x, 1, width, height), 2, 2);
        var fraction = Level is { } measured && double.IsFinite(measured) ? Math.Clamp((measured - floor) / -floor, 0, 1) : 0;
        for (var segment = 0; segment < 24; segment++)
        {
            var lit = Level is not null && segment / 24.0 < fraction;
            var hot = segment >= 22;
            var pitch = (height - 4) / 24;
            var cell = new Rect(x + 2, 3 + (23 - segment) * pitch, width - 4, Math.Max(1, pitch - 2));
            if (lit)
                context.DrawRectangle(hot ? HotHalo : SignalHalo, null, cell.Inflate(2), 3, 3);
            context.DrawRectangle(lit ? (hot ? Hot : Signal) : Idle, null, cell, 1, 1);
        }
        if (Level is null)
        {
            var label = new FormattedText("—", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, Brush.Parse("#A1ADB7"));
            context.DrawText(label, new Point((Bounds.Width - label.Width) / 2, (Bounds.Height - label.Height) / 2));
        }
    }
}
