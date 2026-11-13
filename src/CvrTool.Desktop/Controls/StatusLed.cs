// Desktop / Status LED — on, off and unknown are visually distinct states.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CvrTool.Desktop;
/// <summary>A round status light; unknown data stays hollow instead of suggesting a healthy state.</summary>
public sealed class StatusLed(bool? value, Color onColor) : Control
{
    public bool? Value { get; private set; } = value;
    public void Update(bool? next) { if (Value != next) { Value = next; InvalidateVisual(); } }
    public Color OnColor { get; } = onColor;

    protected override Size MeasureOverride(Size availableSize) => new(14, 14);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(6, Math.Min(Bounds.Width, Bounds.Height) / 2);
        var border = new Pen(Brush.Parse("#71808B"), 1);
        if (Value == true)
        {
            var glow = Color.FromArgb(55, OnColor.R, OnColor.G, OnColor.B);
            context.DrawEllipse(new SolidColorBrush(glow), null, center, radius + 1, radius + 1);
            context.DrawEllipse(new SolidColorBrush(OnColor), null, center, radius - 1, radius - 1);
        }
        else if (Value == false)
        {
            context.DrawEllipse(Brush.Parse("#26343D"), border, center, radius - 1, radius - 1);
        }
        else
        {
            context.DrawEllipse(null, border, center, radius - 1, radius - 1);
            context.DrawLine(border, new Point(center.X - 2, center.Y), new Point(center.X + 2, center.Y));
        }
    }
}
