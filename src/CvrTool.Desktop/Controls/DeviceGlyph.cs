// Desktop / Device symbols — vector lock, power, save and cancel icons.
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
