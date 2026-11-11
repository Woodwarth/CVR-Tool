// Desktop / Preset management
// Purpose: load/save native .sd PC files and operate sixteen device preset slots.
// Units: one-based slots; file formats retain complete supported processing state.
// State: bulk loads/reset/clear require an explicit confirmation in the product.
// Scope: no raw JSON text view; errors and confirmed outcomes appear in the footer.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CvrTool.Application;
using CvrTool.Core;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private int selectedPresetSlot = 1;
    private Control PresetsPage()
    {
        var editedId = deviceId;
        var presets = service.GetDevicePresets(editedId);
        var content = new StackPanel { Spacing = 24 };
        content.Children.Add(Heading("Presets · " + deviceName.Text));
        var device = new StackPanel { Spacing = 18 };
        device.Children.Add(Heading("On Device"));
        var selection = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,2*,*"), ColumnSpacing = 20 };
        var slot = new ComboBox { ItemsSource = presets.Slots, SelectedIndex = selectedPresetSlot - 1, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "device-preset-slot", ItemTemplate = new FuncDataTemplate<DevicePresetSlot>((s, _) => new TextBlock { Text = $"{s?.Number:00} · {s?.Name}" }) };
        var name = new TextBox { Text = presets.Slots[selectedPresetSlot - 1].Occupied ? presets.Slots[selectedPresetSlot - 1].Name : "Preset " + selectedPresetSlot, MaxLength = 32, Tag = "device-preset-name" };
        slot.SelectionChanged += (_, _) => { if (slot.SelectedItem is DevicePresetSlot selected) { selectedPresetSlot = selected.Number; name.Text = selected.Occupied ? selected.Name : "Preset " + selected.Number; } };
        AddColumn(selection, Field("Preset slot · 1–16", slot), 0);
        AddColumn(selection, Field("Preset name", name), 1);
        AddColumn(selection, Field("Current preset", Text(presets.CurrentName ?? "—", 14, Accent)), 2);
        device.Children.Add(selection);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        Button Action(string label, string tag, Func<Task> action, bool danger = false)
        {
            var button = new Button { Content = label, Tag = tag, Padding = new Thickness(18, 12), Background = Brush.Parse(danger ? "#762A37" : "#007F7D") };
            button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { status.Text = ex.Message; } finally { button.IsEnabled = true; } };
            return button;
        }
        async Task RunDevice(string operation, Func<DeviceOperationResult> run, string question)
        {
            if (!await ConfirmPresetAction(operation, question))
                return;
            status.Text = operation + "…";
            var result = await Task.Run(run);
            channels = service.GetDevice(editedId).Outputs.Select(c => new ChannelDraft(c)).ToList();
            Render();
            status.Text = result.Message;
        }
        actions.Children.Add(Action("Load from Device", "preset-load-device", () => RunDevice("Load from Device", () => service.RecallDevicePreset(editedId, selectedPresetSlot), "Replace current processing with the selected device preset?")));
        actions.Children.Add(Action("Save to Device", "preset-save-device", () => { var number = selectedPresetSlot; var presetName = name.Text ?? ""; return RunDevice("Save to Device", () => service.StoreDevicePreset(editedId, number, presetName), "Store current processing in this slot? An existing preset in the slot will be replaced."); }));
        actions.Children.Add(Action("Factory Reset", "preset-factory-reset", () => RunDevice("Factory Reset", () => service.FactoryReset(editedId), "Restore this device's factory settings? Current processing and device presets may be cleared."), true));
        actions.Children.Add(Action("Clear All Preset", "preset-clear-all", () => RunDevice("Clear All Preset", () => service.ClearDevicePresets(editedId), "Permanently clear all sixteen stored device presets? Current processing stays active."), true));
        device.Children.Add(actions);
        content.Children.Add(Card(device));
        var pc = new StackPanel { Spacing = 18 };
        pc.Children.Add(Heading("On PC"));
        var files = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        files.Children.Add(Action("Load from PC", "preset-load-pc", async () =>
        {
            var choices = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load preset from PC", AllowMultiple = false, FileTypeFilter = [new("CVR native preset") { Patterns = ["*.sd"] }] });
            if (choices.Count != 1)
                return;
            if (!choices[0].Name.EndsWith(".sd", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Load a preset with the .sd extension.");
            await using var stream = await choices[0].OpenReadAsync();
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(buffer)) > 0)
            {
                if (data.Length + length > NativePresetCodec.MaximumBytes)
                    throw new InvalidDataException("Preset exceeds 1 MiB.");
                data.Write(buffer, 0, length);
            }
            if (!await ConfirmPresetAction("Load from PC", "Replace current device processing with " + choices[0].Name + "?"))
                return;
            await Task.Run(() => service.LoadPresetFile(editedId, choices[0].Name, data.ToArray()));
            nativePresetName = choices[0].Name;
            ReloadSession();
            status.Text = "Preset loaded · " + choices[0].Name;
        }));
        files.Children.Add(Action("Save to PC", "preset-save-pc", async () =>
        {
            const string extension = "sd";
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save preset to PC", SuggestedFileName = deviceName.Text + "." + extension, DefaultExtension = extension, FileTypeChoices = [new("CVR native preset") { Patterns = ["*." + extension] }] });
            if (file is null)
                return;
            if (!file.Name.EndsWith(".sd", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Save the preset with the .sd extension.");
            var bytes = await Task.Run(() => service.ExportNativePreset(editedId));
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
            status.Text = "Preset saved · " + file.Name;
        }));
        pc.Children.Add(files);
        content.Children.Add(Card(pc));
        return content;
    }
    private async Task<bool> ConfirmPresetAction(string title, string text)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        panel.Children.Add(Heading(title));
        var description = Text(text, 14, Muted);
        description.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(description);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var confirm = new Button { Content = title, Tag = "confirm-preset-operation" };
        var cancel = new Button { Content = "Cancel", Tag = "cancel-preset-operation" };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        var dialog = EditorWindow(title, panel, 560, 240);
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
    }
    public string ExportSelectedPreset() => service.ExportPreset(deviceId);
    public void LoadPresetFile(string name, byte[] bytes)
    {
        service.LoadPresetFile(deviceId, name, bytes);
        nativePresetName = Path.GetFileName(name);
        ReloadSession();
    }
}
