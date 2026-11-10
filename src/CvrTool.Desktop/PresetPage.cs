// Desktop / Presets — file dialogs, native inspection and demo JSON export.
// Purpose: present focused controls and forward edits to Application.
// Input: user events and authoritative device snapshots.
// State: local drafts never replace the shared backend.
// Scope: UI rendering does not establish physical hardware capability.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private Control PresetsPage()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 18
        };
        panel.Children.Add(Heading("Presets · Local files"));
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
        };
        var export = new Button
        {
            Content = "Save preset as JSON",
            Background = Brush.Parse("#007F7D")
        };
        export.Click += async (_, _) =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save preset", SuggestedFileName = deviceName.Text + "-preset.json", DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("JSON preset") { Patterns = ["*.json"] }] });
                if (file is null)
                    return;
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await using var writer = new System.IO.StreamWriter(stream);
                await writer.WriteAsync(service.ExportPreset(deviceId));
                status.Text = "Preset saved · " + file.Name;
            }
            catch (Exception ex)
            {
                status.Text = "Export failed: " + ex.Message;
            }
        };
        var inspect = new Button
        {
            Content = "Inspect native preset (.sd)"
        };
        inspect.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Inspect native preset locally", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("AMP Controller preset") { Patterns = ["*.sd"] }] });
                if (files.Count != 1)
                    return;
                await using var stream = await files[0].OpenReadAsync();
                ShowNativePreset(files[0].Name, await NativePresetInspector.InspectAsync(stream));
            }
            catch (Exception ex)
            {
                status.Text = "Preset inspection failed: " + ex.Message;
            }
        };
        export.Margin = new Thickness(0, 0, 12, 6);
        inspect.Margin = new Thickness(0, 0, 12, 6);
        toolbar.Children.Add(export);
        toolbar.Children.Add(inspect);
        Grid.SetRow(toolbar, 1);
        panel.Children.Add(toolbar);
        var info = new StackPanel
        {
            Spacing = 8
        };
        if (nativePreset is { } report)
        {
            info.Children.Add(Text(nativePresetName ?? "Native preset", 16, weight: FontWeight.SemiBold));
            info.Children.Add(Text($"Model hint: {report.ModelHint ?? "not detected"} · {report.ByteLength:N0} bytes · Read-only preview", 13, Accent));
            info.Children.Add(Text("SHA-256: " + report.Sha256, 11, Muted));
            info.Children.Add(Text(report.Markers.Count == 0 ? "No known text markers found." : string.Join(" · ", report.Markers.Select(m => $"{m.Text} at offset {m.Offset}")), 12, Muted));
            if (report.ModelHint is { } hint && hint != deviceName.Text)
                info.Children.Add(Text("Model hint differs from the selected device.", 13, Brush.Parse("#F0BD71")));
        }
        else
            info.Children.Add(Text("Inspect native presets locally; the original file remains unchanged.", 13, Muted));
        info.Children.Add(Text(".sd parameters are not decoded yet. No parameters are imported. JSON below contains the current application snapshot.", 12, Muted));
        Grid.SetRow(info, 2);
        panel.Children.Add(info);
        var preview = new TextBox
        {
            Text = service.ExportPreset(deviceId),
            IsReadOnly = true,
            MaxHeight = 480,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 13,
            Padding = new Thickness(18)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        Grid.SetRow(preview, 3);
        panel.Children.Add(preview);
        return Card(panel);
    }

    public void ShowNativePreset(string name, NativePresetInspection inspection)
    {
        nativePresetName = System.IO.Path.GetFileName(name);
        nativePreset = inspection;
        activeTab = "Presets";
        status.Text = "Preset inspected locally · No parameters applied · No network access";
        Render();
    }
}
