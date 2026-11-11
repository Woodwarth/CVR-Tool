// Desktop / Device header — inline name editing and per-device demo control symbols.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private void ShowDeviceName()
    {
        var button = new Button
        {
            Content = Text(service.GetDevice(deviceId).Name, 16),
            Tag = "device-name-display",
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(button, "Edit device name");
        button.Click += (_, _) => EditDeviceName();
        nameSlot.Content = button;
    }

    private void EditDeviceName()
    {
        if (nameEditor.Parent is Panel previous)
            previous.Children.Remove(nameEditor);
        nameEditor.Text = service.GetDevice(deviceId).Name;
        var editor = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,32,32"),
            ColumnSpacing = 6
        };
        editor.Children.Add(nameEditor);
        var save = new Button
        {
            Content = new DeviceGlyph("save"),
            Tag = "save-name",
            Width = 32,
            Height = 32,
            Padding = new Thickness(6)
        };
        var cancel = new Button
        {
            Content = new DeviceGlyph("cancel"),
            Tag = "cancel-name",
            Width = 32,
            Height = 32,
            Padding = new Thickness(6)
        };
        ToolTip.SetTip(save, "Save name · Enter");
        ToolTip.SetTip(cancel, "Cancel · Escape");
        save.Click += (_, _) => SaveDeviceName();
        cancel.Click += (_, _) => ShowDeviceName();
        AddColumn(editor, save, 1);
        AddColumn(editor, cancel, 2);
        nameSlot.Content = editor;
        nameEditor.Focus();
        nameEditor.SelectAll();
    }

    private void SaveDeviceName()
    {
        try
        {
            service.RenameDevice(deviceId, nameEditor.Text ?? "");
            status.Text = "Name saved";
            Render();
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            nameEditor.Focus();
        }
    }

    private Control DeviceActions()
    {
        var state = service.GetControlState(deviceId);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToggleButton Toggle(string kind, bool initial, string label, Action<bool> change)
        {
            var button = new ToggleButton
            {
                Tag = "device-" + kind,
                IsEnabled = !liveReadOnly || (kind == "lock" ? service.GetDevice(deviceId).Readings!.CanSetDisplayLock : service.GetDevice(deviceId).Readings!.CanSetStandby),
                IsChecked = initial,
                Width = 38,
                Height = 38,
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1)
            };
            button.Template = new FuncControlTemplate<ToggleButton>((control, _) => new Border { [!Border.BackgroundProperty] = control[!BackgroundProperty], [!Border.BorderBrushProperty] = control[!BorderBrushProperty], [!Border.BorderThicknessProperty] = control[!BorderThicknessProperty], CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Child = new ContentPresenter { [!ContentPresenter.ContentProperty] = control[!ContentProperty], HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center } });
            void Paint()
            {
                var active = button.IsChecked == true;
                button.Content = new DeviceGlyph(kind, active);
                button.Background = active ? Brush.Parse("#007F7D") : Brush.Parse("#182129");
                button.BorderBrush = active ? Accent : Line;
                AutomationProperties.SetName(button, label + (kind == "standby" && !state.StandbyKnown ? " unknown" : active ? " on" : " off"));
                ToolTip.SetTip(button, label + (active ? ": on" : ": off"));
            }

            Paint();
            button.IsCheckedChanged += (_, _) =>
            {
                change(button.IsChecked == true);
                Paint();
            };
            button.GotFocus += (_, _) => button.BorderThickness = new Thickness(2);
            button.LostFocus += (_, _) => button.BorderThickness = new Thickness(1);
            return button;
        }

        void Set(bool? locked = null, bool? standby = null)
        {
            if (liveReadOnly) { SetLiveControls(locked, standby); return; }
            try
            {
                var after = service.SetControlState(deviceId, locked, standby);
                RefreshDeviceInfo();
                status.Text = $"Display {(after.DisplayLocked ? "locked" : "unlocked")} · Standby {(after.Standby ? "on" : "off")}";
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                deviceActions.Content = Text("State unknown · Select the device to refresh", 12, Muted);
            }
        }

        actions.Children.Add(Toggle("lock", state.DisplayLocked, "Display lock", value => Set(locked: value)));
        actions.Children.Add(Toggle("standby", state.Standby, "Standby", value => Set(standby: value)));
        return actions;
    }
}
