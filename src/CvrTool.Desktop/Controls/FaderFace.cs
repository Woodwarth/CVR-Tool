// Desktop / Fader — draw the gain track and thumb; Slider supplies input behavior.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class FaderFace : Control
{
    public double Value
    {
        get; set;
    }
    public bool HasFocus
    {
        get; set;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var x = Bounds.Width / 2 - 5;
        var top = 12.0;
        var bottom = Math.Max(top + 1, Bounds.Height - 12);
        var y = bottom - (Value + 80) / 98 * (bottom - top);
        context.DrawLine(new Pen(Brush.Parse("#0C1419"), 8), new Point(x, top), new Point(x, bottom));
        context.DrawLine(new Pen(Brush.Parse("#65757E"), 3), new Point(x, top), new Point(x, bottom));
        context.DrawLine(new Pen(Brush.Parse("#00BFAE"), 4), new Point(x, y), new Point(x, bottom));
        for (var i = 0; i <= 16; i++)
        {
            var tickY = top + i * (bottom - top) / 16;
            context.DrawLine(new Pen(Brush.Parse("#93A2A9"), 1), new Point(x + 17, tickY), new Point(x + (i % 4 == 0 ? 27 : 22), tickY));
        }

        var silver = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.Parse("#F6F8FA"), 0),
                new(Color.Parse("#99A4AC"), .45),
                new(Color.Parse("#E0E5E8"), .6),
                new(Color.Parse("#7A8790"), 1)
            }
        };
        context.DrawRectangle(Brush.Parse("#0C1419"), null, new Rect(x - 10, y - 13, 23, 31), 3, 3);
        context.DrawRectangle(silver, new Pen(Brush.Parse("#BDC6CC")), new Rect(x - 10, y - 15, 21, 30), 2, 2);
        context.DrawLine(new Pen(Brush.Parse("#424E57"), 2), new Point(x - 9, y), new Point(x + 10, y));
        if (HasFocus)
            context.DrawRectangle(null, new Pen(Brush.Parse("#00CEBD"), 2), new Rect(x - 14, y - 19, 29, 38), 3, 3);
    }
}
