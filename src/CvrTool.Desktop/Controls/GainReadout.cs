// Desktop / Gain readout — plain dB value with click-to-edit precision entry.
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace CvrTool.Desktop;

public sealed class GainReadout : ContentControl
{
    private double value;
    private bool editing;
    public event Action<double>? ValueChanged;
    public event Action? EditingStarted;
    public event Action? EditingEnded;
    public double Value
    {
        get => value;
        set
        {
            if (!double.IsFinite(value) || value is < -80 or > 18)
                throw new ArgumentOutOfRangeException(nameof(value));
            var changed = this.value != value;
            this.value = value;
            if (!editing)
                ShowValue();
            if (changed)
                ValueChanged?.Invoke(value);
        }
    }

    public GainReadout()
    {
        Foreground = Appearance.Ink;
        FontSize = 16;
        FontWeight = FontWeight.SemiBold;
        Width = 128;
        Height = 36;
        AutomationProperties.SetName(this, "Channel gain in dB");
        ShowValue();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        // A navigation change must not leave live refresh paused by an abandoned draft.
        if (editing)
        {
            editing = false;
            EditingEnded?.Invoke();
        }
        base.OnDetachedFromLogicalTree(e);
    }

    private void ShowValue()
    {
        var wasEditing = editing;
        editing = false;
        if (wasEditing) EditingEnded?.Invoke();
        var label = new TextBlock { Text = value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB", Foreground = Foreground, FontSize = FontSize, FontWeight = FontWeight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var edit = new Button { Tag = "gain-edit", Content = label, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(edit, "Click to change gain · −80 to +18 dB");
        edit.Click += (_, _) => BeginEdit();
        Content = edit;
    }

    public void BeginEdit()
    {
        if (editing || !IsEffectivelyEnabled) return;
        editing = true;
        EditingStarted?.Invoke();
        var editor = new TextBox
        {
            Text = value.ToString("0.0", CultureInfo.InvariantCulture),
            FontSize = 14,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(6, 4),
            BorderBrush = Brush.Parse("#00CEBD"),
            Background = Appearance.Surface
        };
        void Commit()
        {
            if (!editing)
                return;
            if (!double.TryParse(editor.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed is < -80 or > 18)
            {
                ToolTip.SetTip(editor, "Enter a number from −80 to +18 dB");
                editor.BorderBrush = Brush.Parse("#FF566A");
                return;
            }

            Value = parsed;
            ShowValue();
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down && double.TryParse(editor.Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
            {
                editor.Text = Math.Clamp(current + (e.Key == Key.Up ? .5 : -.5), -80, 18).ToString("0.0", CultureInfo.InvariantCulture);
                editor.SelectAll(); e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ShowValue();
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => Commit();
        Content = editor;
        editor.Focus();
        editor.SelectAll();
    }
}
