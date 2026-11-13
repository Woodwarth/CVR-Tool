// Desktop / Routing wires — visual alignment only; these lines do not represent verified hardware topology.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class RoutingConnections(int outputs, double firstRow = 113, double rowStep = 112, double inputWidth = 170, double outputWidth = 170) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(Brush.Parse("#46525D"), 2);
        for (var row = 0; row < outputs; row++)
        {
            var y = firstRow + row * rowStep;
            context.DrawLine(pen, new Point(0, y), new Point(inputWidth, y));
            context.DrawLine(pen, new Point(Bounds.Width - outputWidth, y), new Point(Bounds.Width, y));
        }
    }
}
