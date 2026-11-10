// Desktop / Routing wires — visual alignment only; these lines do not represent verified hardware topology.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class RoutingConnections(int outputs) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(Brush.Parse("#46525D"), 2);
        for (var row = 0; row < outputs; row++)
        {
            var y = 113 + row * 112;
            context.DrawLine(pen, new Point(0, y), new Point(170, y));
            context.DrawLine(pen, new Point(Bounds.Width - 170, y), new Point(Bounds.Width, y));
        }
    }
}
