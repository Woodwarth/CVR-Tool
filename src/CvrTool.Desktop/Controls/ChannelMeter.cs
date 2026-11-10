// Desktop / Meter — segmented dBFS display; missing data never lights a segment.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class ChannelMeter(double? level) : Control
{
    public override void Render(DrawingContext context)
    {
        var width = Math.Min(22, Math.Max(10, Bounds.Width - 10));
        var x = (Bounds.Width - width) / 2;
        var height = Math.Max(10, Bounds.Height - 4);
        context.DrawRectangle(Brush.Parse("#0D161D"), new Pen(Brush.Parse("#53646F")), new Rect(x, 1, width, height), 2, 2);
        var fraction = level is { } measured && double.IsFinite(measured) ? Math.Clamp((measured + 60) / 60, 0, 1) : 0;
        for (var segment = 0; segment < 24; segment++)
        {
            var lit = level is not null && segment / 24.0 < fraction;
            var brush = Brush.Parse(lit ? segment >= 22 ? "#FF5264" : "#00CEBD" : "#20383E");
            context.DrawRectangle(brush, null, new Rect(x + 3, 3 + (23 - segment) * (height - 4) / 24, width - 6, Math.Max(1, (height - 4) / 24 - 2)));
        }
    }
}
