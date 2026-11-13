// Desktop / Routing dial — one gain/mute interaction model for every matrix cell.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
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
/// <summary>Route gain dial. Drag vertically, scroll, or use the keyboard; click to toggle mute.</summary>
public sealed class RoutingDial : Control
{
    private double gain;
    private bool muted;
    private Point? pressed;
    private double startGain;
    private bool dragging;
    public bool DeferGainUntilRelease { get; set; }
    public event Action? InteractionStarted;
    public event Action? InteractionEnded;
    public bool CanEditGain { get; set; } = true;
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
        LostFocus += (_, _) => EndEntry();
        Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        ToolTip.SetTip(this, "Drag up/down or scroll to adjust gain · Click to mute · Type dB and press Enter · Arrows, wheel and page keys: 0.5 dB · Space: mute");
    }

    private void Adjust(double value)
    {
        if (!CanEditGain) return;
        var next = Math.Round(Math.Clamp(value, -80, 18) * 2, MidpointRounding.AwayFromZero) / 2;
        if (next == gain)
            return;
        GainDb = next;
        if (!DeferGainUntilRelease || pressed is null) GainChanged?.Invoke(next);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus();
        pressed = e.GetPosition(this);
        startGain = gain;
        InteractionStarted?.Invoke();
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
        FinishInteraction();
        e.Pointer.Capture(null);
        if (toggle)
            MuteRequested?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        FinishInteraction();
        base.OnPointerCaptureLost(e);
    }

    private void FinishInteraction()
    {
        if (pressed is null) return;
        var changed = dragging && gain != startGain;
        pressed = null;
        dragging = false;
        InteractionEnded?.Invoke();
        if (DeferGainUntilRelease && changed) GainChanged?.Invoke(gain);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!CanEditGain) return;
        Adjust(gain + Math.Sign(e.Delta.Y) * .5);
        e.Handled = true;
    }

    private string? typedGain;
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!CanEditGain || string.IsNullOrEmpty(e.Text) || !e.Text.All(c => "0123456789-+.,".Contains(c))) return;
        if (typedGain is null) { typedGain = ""; InteractionStarted?.Invoke(); }
        if (typedGain.Length < 8) typedGain += e.Text.Replace(',', '.');
        InvalidateVisual();
        e.Handled = true;
    }
    private void EndEntry()
    {
        if (typedGain is null) return;
        typedGain = null;
        InteractionEnded?.Invoke();
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (typedGain is not null)
        {
            if (e.Key == Key.Enter && double.TryParse(typedGain, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) && db is >= -80 and <= 18)
            { EndEntry(); Adjust(db); }
            else if (e.Key == Key.Escape) EndEntry();
            else if (e.Key == Key.Back && typedGain.Length > 0) typedGain = typedGain[..^1];
            InvalidateVisual(); e.Handled = true; return;
        }
        switch (e.Key)
        {
            case Key.Up:
            case Key.Right:
                Adjust(gain + .5);
                break;
            case Key.Down:
            case Key.Left:
                Adjust(gain - .5);
                break;
            case Key.PageUp:
                Adjust(gain + .5);
                break;
            case Key.PageDown:
                Adjust(gain - .5);
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
        if (muted && typedGain is null)
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
            var text = new FormattedText(typedGain ?? gain.ToString("0.#", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White);
            context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }
}
