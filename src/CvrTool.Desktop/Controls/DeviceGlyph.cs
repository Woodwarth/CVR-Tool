// Desktop / Device symbols — vector lock, power, waving hand, save and cancel icons.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class DeviceGlyph(string kind, bool active = false) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(Brush.Parse("#EDF2F5"), 1.7);
        switch (kind)
        {
            case "lock":
                context.DrawRectangle(null, pen, new Rect(3, 8, 14, 10), 2, 2);
                context.DrawGeometry(null, pen, Geometry.Parse(active ? "M 6,8 L 6,5 C 6,0 14,0 14,5 L 14,8" : "M 6,8 L 6,5 C 6,0 14,0 14,5"));
                context.DrawLine(pen, new Point(10, 11), new Point(10, 15));
                break;
            case "standby":
                context.DrawGeometry(null, pen, Geometry.Parse("M 5,4 C -2,10 2,18 10,18 C 18,18 22,10 15,4 M 10,0 L 10,10"));
                break;
            case "wink":
                var handPen = new Pen(Brush.Parse("#EDF2F5"), 1.25);
                context.DrawGeometry(null, handPen, Geometry.Parse("M 7,18 C 5,17 4,15 3,12 L 1.7,9.5 Q 1,8 2.2,7.6 Q 3,7.2 3.8,8.4 L 5.5,10.5 L 3.7,4.6 Q 3.3,3.3 4.4,3 Q 5.5,2.7 6,4 L 7.3,8 L 6.3,2.5 Q 6.1,1.3 7.2,1.1 Q 8.3,1 8.5,2.3 L 9.5,7.8 L 9.6,2.5 Q 9.6,1.3 10.7,1.3 Q 11.8,1.3 11.8,2.6 L 11.8,8 L 12.9,4 Q 13.2,2.9 14.3,3.2 Q 15.4,3.5 15.1,4.8 L 13.7,10.8 C 14.7,14.8 13.5,17.8 10,18.5 Z"));
                context.DrawGeometry(null, handPen, Geometry.Parse("M 16.5,2 Q 20,5 18.5,9 M 17,4 Q 18.5,6 17.5,8"));
                break;
            case "save":
                context.DrawGeometry(null, pen, Geometry.Parse("M 2,10 L 7,15 L 18,3"));
                break;
            case "cancel":
                context.DrawGeometry(null, pen, Geometry.Parse("M 3,3 L 17,17 M 17,3 L 3,17"));
                break;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(20, 20);
}
