// Desktop / Speaker — vector mute/unmute symbol, independent of installed fonts.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;

internal sealed class SpeakerGlyph(bool muted) : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Brushes.White, 1.5);
        context.DrawGeometry(null, pen, Geometry.Parse("M 1,7 L 5,7 L 10,3 L 10,15 L 5,11 L 1,11 Z"));
        if (muted)
        {
            context.DrawLine(pen, new Point(13, 6), new Point(18, 12));
            context.DrawLine(pen, new Point(18, 6), new Point(13, 12));
        }
        else
            context.DrawGeometry(null, pen, Geometry.Parse("M 13,5 Q 18,9 13,13"));
    }
}
