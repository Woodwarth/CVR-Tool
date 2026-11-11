// Desktop / Gain readout — plain dB value with click-to-edit precision entry.
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
using Avalonia.Media;

namespace CvrTool.Desktop;

public sealed class GainReadout : ContentControl
{
    private double value;
    private bool editing;
    public event Action<double>? ValueChanged;
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
        Width = 128;
        Height = 36;
        AutomationProperties.SetName(this, "Channel gain in dB");
        ShowValue();
    }

    private void ShowValue()
    {
        editing = false;
        var label = new TextBlock { Text = value.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture)+" dB", FontSize=16, FontWeight=FontWeight.SemiBold, HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        var edit = new Button { Tag="gain-edit",Content=label,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Stretch };
        ToolTip.SetTip(edit,"Click to change gain · −80 to +18 dB"); edit.Click+=(_,_)=>Edit();Content=edit;
    }

    private void Edit()
    {
        editing = true;
        var editor = new TextBox
        {
            Text = value.ToString("0.0", CultureInfo.InvariantCulture),
            FontSize = 14,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(6, 4),
            BorderBrush = Brush.Parse("#00CEBD"),
            Background = Brush.Parse("#101B23")
        };
        void Commit()
        {
            if (!editing)
                return;
            if (!double.TryParse(editor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed is < -80 or > 18)
            {
                ToolTip.SetTip(editor, "Enter a number from −80 to +18 dB");
                editor.BorderBrush = Brush.Parse("#FF566A");
                return;
            }

            editing = false;
            Value = parsed;
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
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
