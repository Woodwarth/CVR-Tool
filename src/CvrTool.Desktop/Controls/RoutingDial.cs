// Desktop / Routing dial — one gain/mute interaction model for every matrix cell.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CvrTool.Desktop;
/// <summary>Demo route gain dial. Drag vertically, scroll, or use the keyboard; click to toggle mute.</summary>
public sealed class RoutingDial : Control
{
    private double gain;
    private bool muted;
    private Point? pressed;
    private double startGain;
    private bool dragging;
    public event Action<double>? GainChanged;
    public event Action? MuteRequested;
    public double GainDb
    {
        get => gain;
        set
        {
            gain = Math.Clamp(value, -80, 18);
            InvalidateVisual();
        }
    }

    public bool Muted
    {
        get => muted;
        set
        {
            muted = value;
            InvalidateVisual();
        }
    }

    public RoutingDial()
    {
        Width = 74;
        Height = 74;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        ToolTip.SetTip(this, "Drag up/down or scroll to adjust gain · Click to mute · Arrow keys: 0.1 dB · Page keys: 1 dB · Space: mute");
    }

    private void Adjust(double value)
    {
        var next = Math.Round(Math.Clamp(value, -80, 18), 1);
        if (next == gain)
            return;
        GainDb = next;
        GainChanged?.Invoke(next);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        pressed = e.GetPosition(this);
        startGain = gain;
        dragging = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressed is not { } origin)
            return;
        var distance = origin.Y - e.GetPosition(this).Y;
        if (Math.Abs(distance) > 3)
            dragging = true;
        if (dragging)
            Adjust(startGain + distance * .5);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressed is null)
            return;
        var toggle = !dragging;
        pressed = null;
        e.Pointer.Capture(null);
        if (toggle)
            MuteRequested?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        pressed = null;
        dragging = false;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Adjust(gain + e.Delta.Y);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Up:
            case Key.Right:
                Adjust(gain + .1);
                break;
            case Key.Down:
            case Key.Left:
                Adjust(gain - .1);
                break;
            case Key.PageUp:
                Adjust(gain + 1);
                break;
            case Key.PageDown:
                Adjust(gain - 1);
                break;
            case Key.Home:
                Adjust(-80);
                break;
            case Key.End:
                Adjust(18);
                break;
            case Key.Space:
            case Key.M:
                MuteRequested?.Invoke();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 9;
        context.DrawEllipse(null, new Pen(Brush.Parse("#46525D"), 8), center, radius, radius);
        var fraction = (gain + 80) / 98;
        Point At(double angle) => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        const double start = Math.PI * .75;
        var end = start + fraction * Math.PI * 1.5;
        if (!muted && fraction > 0)
        {
            var arc = new StreamGeometry();
            using (var path = arc.Open())
            {
                path.BeginFigure(At(start), false);
                for (var step = 1; step <= 96; step++)
                    path.LineTo(At(start + (end - start) * step / 96));
                path.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(Brush.Parse("#00CEBD"), 7), arc);
        }

        context.DrawEllipse(Brush.Parse("#202830"), new Pen(Brush.Parse(muted ? "#ED5D6F" : "#EDF2F5"), IsFocused ? 4 : 2.5), center, radius - 6, radius - 6);
        context.DrawEllipse(Brush.Parse("#111C22"), new Pen(Brushes.White, 1.5), At(end), 5, 5);
        if (muted)
        {
            // Mute hides the number without discarding the stored gain.
            using (context.PushTransform(Matrix.CreateTranslation(center.X - 9, center.Y - 9)))
            {
                var pen = new Pen(Brush.Parse("#ED5D6F"), 1.8);
                context.DrawGeometry(null, pen, Geometry.Parse("M 1,7 L 5,7 L 10,3 L 10,15 L 5,11 L 1,11 Z M 13,6 L 18,12 M 18,6 L 13,12"));
            }
        }
        else
        {
            var text = new FormattedText(gain.ToString("0.#", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White);
            context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }
}
