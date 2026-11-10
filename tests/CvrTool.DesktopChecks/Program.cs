using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using CvrTool.Application;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CvrTool.Core;
using CvrTool.Desktop;
using System.Text.Json;

AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
var window = new MainWindow(); window.Show();
var root = (Control)window.Content!;
var count = 0;
void Layout(int width = 1680, int height = 960)
{
    window.Width = width; window.Height = height; Dispatcher.UIThread.RunJobs();
    root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
}
IEnumerable<T> All<T>() where T : class => root.GetLogicalDescendants().OfType<T>();
T Tagged<T>(string tag) where T : Control => All<T>().Single(c => c.Tag?.ToString() == tag);
Button Button(string content) => All<Button>().Single(b => b.Content?.ToString() == content);
void Press(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout(); }
void Click(string content) => Press(Button(content));
void Device(string id) => Press(Tagged<Button>(id));
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS: " + message); }
bool Text(string value) => All<TextBlock>().Any(t => t.Text?.Contains(value) == true);
void Save(string name, int width = 1680, int height = 960)
{
    Directory.CreateDirectory("artifacts"); Layout(width, height);
    using var image = new RenderTargetBitmap(new PixelSize(width, height)); image.Render(root);
    image.Save(Path.Combine("artifacts", name + ".png"), PngBitmapEncoderOptions.Default);
}
PresetDocument Preset() { Click("☰   Presets"); return JsonSerializer.Deserialize<PresetDocument>(All<TextBox>().Single(t => t.IsReadOnly).Text!)!; }
Layout(); Save("main-1004");
var fourChannelSize = Tagged<Border>("input-channel-A").Bounds.Size;
Check(window.Icon is not null, "Window icon loads from embedded application assets");
Check(All<TextBlock>().Count(t => t.Tag?.ToString()?.StartsWith("info-") == true) == 4, "Compact UI identity header keeps source documentation out of the interface");
var gainReadout = Tagged<GainReadout>("gain-A");
Button GainButton(string tag) => gainReadout.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == tag);
Press(GainButton("gain-step-up"));
Check(Preset().Outputs[0].GainDb == .1, "Compact gain plus button commits immediately");
Click("⌂   Main"); gainReadout = Tagged<GainReadout>("gain-A"); Press(GainButton("gain-edit"));
var precisionInput = gainReadout.GetLogicalDescendants().OfType<TextBox>().Single(); precisionInput.Text = "-7.5";
precisionInput.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Layout();
Check(Preset().Outputs[0].GainDb == -7.5, "Click-to-edit dB readout commits precision entry");
Click("⌂   Main"); gainReadout = Tagged<GainReadout>("gain-A"); Press(GainButton("gain-edit"));
precisionInput = gainReadout.GetLogicalDescendants().OfType<TextBox>().Single(); precisionInput.Text = "99";
precisionInput.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
Check(gainReadout.Value == -7.5 && gainReadout.GetLogicalDescendants().OfType<TextBox>().Any(), "Out-of-range precision entry remains editable without applying");
precisionInput.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Layout();
Check(All<Slider>().Count() == 4, "Four channel faders on Main");
Check(All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("channel-meter-") == true) == 4 && All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("channel-clip-") == true) == 4, "Every channel fader has a meter and clip status");
Check(All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("telemetry-") == true) == 16, "Four measurements per output");
Check(All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("output-status-") == true) == 32, "Eight independent status flags per output");
var mute = Tagged<ToggleButton>("mute-output-A");
Check(mute.Background is ISolidColorBrush brush && brush.Color == Color.Parse("#BE3044") && mute.GetLogicalDescendants().OfType<Control>().Any(c => c.GetType().Name == "SpeakerGlyph"), "Mute is red and includes a speaker symbol");
Check(!All<Button>().Any(b => b.Content?.ToString() == "Apply levels") && Text("Inputs") && !Text("No live input data"), "Main has two sections and no Apply levels button");
Check(All<ToggleButton>().Where(b => b.Tag?.ToString()?.StartsWith("mute-") == true).All(b => b.Width <= 32 && b.Height <= 28 && !b.GetLogicalDescendants().OfType<TextBlock>().Any()), "Mute buttons are compact and symbol-only");
Check(All<Border>().Count(b => b.Tag?.ToString()?.StartsWith("input-channel-") == true) == 4 && All<Border>().Count(b => b.Tag?.ToString()?.StartsWith("output-channel-") == true) == 4, "Each Main channel has its own visual group");
foreach (var name in new[] { "A", "B", "C", "D" })
{
    var inputMeter = Tagged<Control>("channel-meter-" + name); var outputMeter = Tagged<Control>("output-meter-" + name);
    var flags = Tagged<Control>("output-flags-" + name);
    var inputGroup = Tagged<Border>("input-channel-" + name);
    var outputGroup = Tagged<Border>("output-channel-" + name);
    var muteControl = Tagged<ToggleButton>("mute-output-" + name);
    var meterInGroup = outputMeter.TranslatePoint(new Point(), outputGroup)!.Value;
    var muteInGroup = muteControl.TranslatePoint(new Point(), outputGroup)!.Value;
    Check(Math.Abs(inputGroup.Bounds.Width - outputGroup.Bounds.Width) < .1 && Math.Abs(meterInGroup.X + outputMeter.Bounds.Width / 2 - outputGroup.Bounds.Width / 2) < .1 && Math.Abs(muteInGroup.X + muteControl.Bounds.Width / 2 - outputGroup.Bounds.Width / 2) < .1 && muteInGroup.Y > meterInGroup.Y + outputMeter.Bounds.Height, "Equal group widths with centered meter and mute underneath: " + name);

    var inputPoint = inputMeter.TranslatePoint(new Point(), root)!.Value;
    var outputPoint = outputMeter.TranslatePoint(new Point(), root)!.Value;
    var flagPoint = flags.TranslatePoint(new Point(), root)!.Value;
    Check(inputMeter.Bounds.Size == outputMeter.Bounds.Size && inputMeter.Bounds.Height == 280 && Math.Abs(inputPoint.Y - outputPoint.Y) < .1 && flagPoint.X > outputPoint.X + outputMeter.Bounds.Width && flagPoint.Y >= outputPoint.Y && flagPoint.Y + flags.Bounds.Height <= outputPoint.Y + outputMeter.Bounds.Height, "Matched meter size/alignment and adjacent statuses: " + name);
}
Tagged<GainReadout>("gain-A").Value = -6;
Check(Preset().Outputs[0].GainDb == -6, "Numeric gain applies immediately and exports");
Click("⌂   Main"); All<Slider>().First().Value = -9; Tagged<ToggleButton>("mute-output-A").IsChecked = false;
Check(Tagged<ToggleButton>("mute-output-A").Background is ISolidColorBrush neutral && neutral.Color == Color.Parse("#182129") && Tagged<ToggleButton>("mute-output-A").BorderBrush is ISolidColorBrush neutralBorder && neutralBorder.Color == Color.Parse("#46525D"), "Unmuted button uses neutral colors without a red border");
var preset = Preset(); Check(preset.Outputs[0] is { GainDb: -9, Muted: false }, "Slider gain applies immediately and mute preserves it");
Click("◉   Inputs"); Save("routing-1004");
Check(All<RoutingDial>().Count() == 16 && !All<NumericUpDown>().Any() && !All<TextBox>().Any() && !All<ToggleButton>().Any(b => b.Tag?.ToString()?.StartsWith("route-") == true), "Matrix has circles only: no external gain fields or mute buttons");
foreach (var dial in All<RoutingDial>().ToArray())
{
    var originalMute = dial.Muted;
    dial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End });
    dial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
    Check(dial.GainDb == 18 && dial.Muted != originalMute, "Same gain and mute behavior: " + dial.Tag);
    dial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
    dial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
    Check(dial.GainDb == -80 && dial.Muted == originalMute, "Every circle supports minimum gain without changing mute: " + dial.Tag);
}
var route = Tagged<RoutingDial>("route-dial-2-1"); route.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
preset = Preset(); Check(preset.Routing!.Single(c => c.Input == 2 && c.Output == 1) is { GainDb: -80, Muted: false }, "Dial-only routing changes persist through shared service");
Click("◉   Inputs"); route = Tagged<RoutingDial>("route-dial-2-1");
route.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End });
route.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space });
Click("⌂   Main"); Click("◉   Inputs");
Check(Tagged<RoutingDial>("route-dial-2-1").GainDb == 18 && Tagged<RoutingDial>("route-dial-2-1").Muted, "Muted route retains maximum gain across views");
Press(Tagged<Button>("device-name-display")); Tagged<TextBox>("device-name").Text = "Front amplifier";
Tagged<TextBox>("device-name").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Layout();
Check(!All<TextBox>().Any() && Tagged<Button>("device-name-display").Content is TextBlock editedName && editedName.Text == "Front amplifier", "Click-to-edit name saves with Enter and returns to plain text");
Press(Tagged<Button>("device-name-display")); Tagged<TextBox>("device-name").Text = "Discard me";
Tagged<TextBox>("device-name").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Layout();
Check(Tagged<Button>("device-name-display").Content is TextBlock kept && kept.Text == "Front amplifier", "Escape cancels name edit");
Press(Tagged<Button>("device-name-display")); Tagged<TextBox>("device-name").Text = " "; Press(Tagged<Button>("save-name"));
Check(All<TextBox>().Any(t => t.Tag?.ToString() == "device-name"), "Invalid name keeps editor open"); Press(Tagged<Button>("cancel-name"));
Device("demo-1502"); Device("demo-1004");
Check(Tagged<Button>("device-name-display").Content is TextBlock retained && retained.Text == "Front amplifier" && Preset().Name == "Front amplifier", "Renamed device survives switching and exports");
Check(Tagged<ToggleButton>("device-lock").IsEnabled && Tagged<ToggleButton>("device-standby").IsEnabled && !Tagged<ToggleButton>("device-lock").GetLogicalDescendants().OfType<TextBlock>().Any(), "Lock and standby are enabled symbol-only controls");
Tagged<ToggleButton>("device-lock").IsChecked = true; Tagged<ToggleButton>("device-standby").IsChecked = true;
Device("demo-1502"); Check(Tagged<ToggleButton>("device-lock").IsChecked == false && Tagged<ToggleButton>("device-standby").IsChecked == false, "Demo lock and standby isolate devices");
Device("demo-1004"); Check(Tagged<ToggleButton>("device-lock").IsChecked == true && Tagged<ToggleButton>("device-standby").IsChecked == true, "Demo lock and standby survive device switching");
Tagged<ToggleButton>("device-lock").IsChecked = false; Tagged<ToggleButton>("device-standby").IsChecked = false;
Click("◖   Outputs"); Save("outputs");
All<NumericUpDown>().Skip(1).First().Value = 4.5m; Press(All<Button>().First(b => b.Content?.ToString() == "Apply"));
Check(Preset().Outputs[0].DelayMs == 4.5, "Output delay applies through shared service");
Device("demo-2002"); Click("⌂   Main");
Check(All<Slider>().Count() == 2 && All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("telemetry-") == true) == 8, "Two-output model has independent measurements");
Check(Tagged<Border>("input-channel-A").Bounds.Size == fourChannelSize && Tagged<Border>("output-channel-A").Bounds.Size == fourChannelSize, "Two- and four-channel models retain identical group dimensions");
Save("main-2002"); Save("main-small", 1080, 680);
Click("◉   Inputs"); Check(All<RoutingDial>().Count() == 4, "DSP-2002 exposes the same two-channel matrix features");
Device("demo-1502");
Check(All<RoutingDial>().Count() == 4, "DSP-1502 has four editable circles"); Save("routing-1502");
Click("≋   EQ"); Check(Enumerable.Range(1, 8).All(n => Text("EQ " + n)), "Eight reference EQ bands"); Save("eq-reference");
var firArgument = Array.IndexOf(args, "--fir");
if (firArgument >= 0)
{
    using var stream = File.OpenRead(args[firArgument + 1]);
    window.ShowFirFile(Path.GetFileName(args[firArgument + 1]), CvrTool.Application.FirFileAnalyzer.AnalyzeAsync(stream).GetAwaiter().GetResult());
    Layout(); Save("fir-preview"); Check(Text("512 coefficients") && Text("Phase · Wrapped"), "English FIR preview includes coefficient count and phase");
}
var presetArgument = Array.IndexOf(args, "--preset");
if (presetArgument >= 0)
{
    Device("demo-1004");
    using var stream = File.OpenRead(args[presetArgument + 1]);
    window.ShowNativePreset(Path.GetFileName(args[presetArgument + 1]), CvrTool.Application.NativePresetInspector.InspectAsync(stream).GetAwaiter().GetResult());
    Layout(); Save("native-preset"); Check(Text("Read-only preview"), "Native preset inspection remains read-only");
    Device("demo-2002"); Check(Text("differs from"), "Native model mismatch is flagged");
}
Click("⌁   Network");
All<TextBox>().Single(t => t.PlaceholderText == "Amplifier IPv4 address").Text = "192.0.2.10";
All<TextBox>().Single(t => t.PlaceholderText == "Local interface IPv4 address").Text = "192.0.2.20";
Click("Validate configuration"); Check(Text("LAN target prepared"), "Offline network target validates in English");
Check(Button("Connect").IsEnabled, "Manual connection is available without scanning"); Save("network-preparation");
Click("⌂   Main"); Click("⌁   Network"); Check(All<TextBox>().Any(t => t.Text == "192.0.2.10"), "Network draft survives navigation");
All<TextBox>().Single(t => t.PlaceholderText == "Amplifier IPv4 address").Text = "localhost"; Click("Validate configuration"); Check(Text("Invalid configuration:"), "Hostname rejected without DNS");
// Uniform scaling preserves design positions and card dimensions at every supported size.
Device("demo-1004"); Click("⌂   Main");
foreach (var (width, height) in new[] { (1008,588), (1080,720), (1680,980), (1920,1080) })
{
    Layout(width,height);
    var canvas = Tagged<Grid>("design-surface");
    var firstGroup = Tagged<Border>("input-channel-A"); var d = Tagged<Border>("output-channel-D");
    var startPoint = firstGroup.TranslatePoint(new Point(), root)!.Value;
    var endPoint = d.TranslatePoint(new Point(d.Bounds.Width,d.Bounds.Height),root)!.Value;
    var origin = canvas.TranslatePoint(new Point(),root)!.Value;
    var corner = canvas.TranslatePoint(new Point(1680,980),root)!.Value;
    Check(canvas.Bounds.Size == new Size(1680,980) && firstGroup.Bounds.Size == fourChannelSize &&
        startPoint.X >= 0 && endPoint.X <= width + 1 && endPoint.Y <= height + 1 &&
        Math.Abs((corner.X-origin.X)/1680-(corner.Y-origin.Y)/980) < .001,
        $"Uniform scale preserves all channel positions and visibility at {width}x{height}");
    Save($"main-scaled-{width}",width,height);
}
Check(window.MinWidth == 1008 && window.MinHeight == 588, "Minimum window size limits excessive shrinking");
Layout(); Click("◉   Inputs");
Check(Text("Input processing") && Text("Delay") && Text("EQ") && Text("Volume A"), "Routing environment includes editable processing and output gain");
Press(Tagged<Button>("input-delay-1"));
var delayEditor = window.OwnedWindows.Single();
var delayMs = delayEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => n.Tag?.ToString() == "delay-ms");
var delayMeters = delayEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => n.Tag?.ToString() == "delay-meters");
delayMeters.Value = 3.43m;
Check(Math.Abs((double)delayMs.Value! - 10) < .0001, "Metres and milliseconds stay synchronized");
delayEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-delay").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
Check(Preset().Inputs![0].DelayMs == 10, "Delay editor commits through the shared input service");
Click("◉   Inputs"); Press(Tagged<Button>("input-eq-1"));
var eqEditor = window.OwnedWindows.Single();
eqEditor.GetLogicalDescendants().OfType<CheckBox>().Single(b=>b.Tag?.ToString()=="input-eq-enabled").IsChecked=false;
eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(b=>b.Tag?.ToString()=="eq-gain-1").Value=3;
eqEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-input-eq").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
Check(Preset().Inputs![0] is { EqEnabled:false } processed && processed.Bands[0].GainDb == 3, "Input EQ dialog applies enable state and band edits");
Click("◉   Inputs");
Tagged<GainReadout>("routing-output-gain-A").Value=-4;
Check(Preset().Outputs[0].GainDb == -4, "Inputs output gain is editable and persists");
Click("◉   Inputs"); Save("routing-editable-inputs");
window.Close();
window = new MainWindow(new AmplifierService(new TelemetryFixture())); window.Show(); root = (Control)window.Content!; Layout();
var clipped = Tagged<Control>("channel-clip-A").GetLogicalDescendants().OfType<TextBlock>().ToArray();
var notClipped = Tagged<Control>("channel-clip-B").GetLogicalDescendants().OfType<TextBlock>().ToArray();
Check(clipped.All(t => t.Foreground is ISolidColorBrush b && b.Color == Color.Parse("#FF566A")) && notClipped.All(t => t.Foreground is ISolidColorBrush b && b.Color != Color.Parse("#FF566A")), "Clip indicator and label turn red only on clipping");
Check(Tagged<Control>("channel-clip-A").GetLogicalDescendants().OfType<StatusLed>().Single().Value == true && Tagged<Control>("channel-clip-B").GetLogicalDescendants().OfType<StatusLed>().Single().Value == false && Tagged<Control>("channel-clip-C").GetLogicalDescendants().OfType<StatusLed>().Single().Value is null && All<StatusLed>().Count() == 36, "Clipping and all output status bits use LEDs with distinct on/off/unknown states");
Check(Tagged<Control>("telemetry-A-0") is TextBlock a && a.Text == "42.0" && Tagged<Control>("telemetry-B-0") is TextBlock b && b.Text == "21.0", "Each output displays its own telemetry");
Save("main-clipping-test-fixture"); window.Close(); Console.WriteLine($"PASS: {count} desktop checks");

// Synthetic values are confined to this UI test; the default demo returns unknown telemetry.
sealed class TelemetryFixture : IAmplifierBackend
{
    private readonly DemoBackend demo = new();
    public IReadOnlyList<DeviceSnapshot> ListDevices() => demo.ListDevices();
    public DeviceSnapshot GetDevice(string id) => demo.GetDevice(id);
    public (ChannelState State, long Revision) GetChannel(string id, int channel) => demo.GetChannel(id, channel);
    public (InputProcessingState State, long Revision) GetInputProcessing(string id, int channel) => demo.GetInputProcessing(id, channel);
    public InputProcessingState ApplyInputProcessing(InputProcessingChange change, long revision) => demo.ApplyInputProcessing(change, revision);
    public DeviceControlState GetControlState(string id) => demo.GetControlState(id);
    public DeviceControlState SetControlState(string id, bool? locked, bool? standby, long revision) => demo.SetControlState(id, locked, standby, revision);
    public ChannelState Apply(ChannelChange change, long revision) => demo.Apply(change, revision);
    public RoutingSnapshot GetRouting(string id) => demo.GetRouting(id);
    public RoutingCellState ApplyRouting(RoutingChange change, long revision) => demo.ApplyRouting(change, revision);
    public DeviceSnapshot Rename(string id, string name, long revision) => demo.Rename(id, name, revision);
    public IReadOnlyList<OutputTelemetry> GetTelemetry(string id) => demo.GetTelemetry(id).Select(t => t.Channel switch
    {
        1 => t with { LevelDbFs = -1, Voltage = 42, Current = 5.25, LoadOhms = 8, TemperatureC = 55, Status = new OutputStatus(Clip: true) },
        2 => t with { LevelDbFs = -12, Voltage = 21, Current = 2.625, LoadOhms = 8, TemperatureC = 40, Status = new OutputStatus(Clip: false) },
        _ => t
    }).ToArray();
}
