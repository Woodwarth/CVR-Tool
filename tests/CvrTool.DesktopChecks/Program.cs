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
PresetDocument Preset() { Click("☰   Presets"); return JsonSerializer.Deserialize<PresetDocument>(window.ExportSelectedPreset())!; }
Layout(); Save("main-1004");
var fourChannelSize = Tagged<Border>("input-channel-A").Bounds.Size;
Check(window.Icon is not null, "Window icon loads from embedded application assets");
Check(All<TextBlock>().Count(t => t.Tag?.ToString()?.StartsWith("info-") == true) == 4, "Compact UI identity header keeps source documentation out of the interface");
Check(!Text("Changes apply immediately") && !Text("No live measurements"), "Main section footer hints are removed");
Check(!All<GainReadout>().Any() && !All<TextBox>().Any() && Tagged<TextBlock>("gain-A").Text == "0.0 dB", "Main gains are plain text without textbox borders or edit controls");
var mainSlider = All<Slider>().First();
mainSlider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up }); Layout();
Check(Preset().Outputs[0].GainDb == .5, "Main arrow-up changes gain by 0.5 dB");
Click("⌂   Main"); mainSlider = All<Slider>().First();
mainSlider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down }); Layout();
Check(Preset().Outputs[0].GainDb == 0, "Main arrow-down changes gain by 0.5 dB"); Click("⌂   Main");
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
All<Slider>().First().Value = -6;
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
Press(Tagged<Button>("output-delay-A"));
var outputDelay=window.OwnedWindows.Single();
outputDelay.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="delay-ms").Value=4.5m;
outputDelay.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-delay").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
Check(Preset().Outputs[0].DelayMs == 4.5, "Output delay applies through shared service");
Click("◖   Outputs");
Press(Tagged<Button>("output-eq-A"));var outputEq=window.OwnedWindows.Single();Press(Tagged<Button>("output-eq-A"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(outputEq,window.OwnedWindows.Single()),"Output EQ reuses its channel editor");
Check(outputEq.GetLogicalDescendants().OfType<ComboBox>().Count(c=>c.Tag?.ToString() is "hp-type" or "lp-type")==2,"Output EQ includes HP and LP controls");
outputEq.GetLogicalDescendants().OfType<EqualizerGraph>().Single().MoveBand(2,2000,-5);
outputEq.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-input-eq").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().OutputProcessing![0].Bands[1].GainDb==-5 && Preset().Inputs![0].Bands[1].GainDb==0,"Output EQ applies independently of input EQ");
Click("◖   Outputs");Press(Tagged<Button>("output-fir-A"));var firWindow=window.OwnedWindows.Single();Press(Tagged<Button>("output-fir-A"));
Check(window.OwnedWindows.Count==1 && firWindow.GetLogicalDescendants().OfType<ResponsePlot>().Count()==3,"FIR has one editor with impulse, frequency and phase plots");
Check(firWindow.GetLogicalDescendants().OfType<Button>().Count(b=>b.Tag?.ToString() is "fir-import" or "fir-export" or "fir-remove")==3,"FIR exposes import, export and remove controls");
firWindow.GetLogicalDescendants().OfType<CheckBox>().Single(b=>b.Tag?.ToString()=="fir-enabled").IsChecked=true;
using(var renderedFir=new RenderTargetBitmap(new PixelSize(1140,780))) {firWindow.Measure(new Size(1140,780));firWindow.Arrange(new Rect(0,0,1140,780));firWindow.UpdateLayout();renderedFir.Render(firWindow);renderedFir.Save("artifacts/output-fir.png",PngBitmapEncoderOptions.Default);}
firWindow.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-fir").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().Firs![0].Enabled,"FIR enable applies through the shared service");
Click("☰   Presets");Save("presets");
Check(!All<TextBox>().Any(t=>t.IsReadOnly) && All<Button>().Count(b=>b.Tag?.ToString()?.StartsWith("preset-")==true)==6 && Tagged<ComboBox>("device-preset-slot").Items.Count==16,"Presets has six actions and sixteen slots without a raw text view");
Press(Tagged<Button>("preset-factory-reset"));var confirmation=window.OwnedWindows.Single();
confirmation.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="cancel-preset-operation").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().Firs![0].Enabled,"Cancelling factory reset leaves processing unchanged");
Click("◉   Inputs");var gainReadout=Tagged<GainReadout>("routing-output-gain-A");
Check(!gainReadout.GetLogicalDescendants().OfType<TextBox>().Any(),"Inputs gain starts as plain text");
Press(gainReadout.GetLogicalDescendants().OfType<Button>().Single());var gainEditor=gainReadout.GetLogicalDescendants().OfType<TextBox>().Single();gainEditor.Text="-7.5";
gainEditor.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Layout();
Check(Preset().Outputs[0].GainDb==-7.5 && !gainReadout.GetLogicalDescendants().OfType<TextBox>().Any(),"Click-to-edit gain saves and returns to plain text");
Click("◖   Outputs");Press(Tagged<Button>("output-mode-A"));var modeEditor=window.OwnedWindows.Single();Press(Tagged<Button>("output-mode-A"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(modeEditor,window.OwnedWindows.Single()),"Repeated mode clicks reuse this output's editor");
var modeChoice=modeEditor.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.Tag?.ToString()=="output-mode-choice");
Check(modeChoice.Items.Cast<string>().SequenceEqual(new[]{"Low-Z","70V","100V"}),"Mode offers Low-Z, 70V and 100V");modeChoice.SelectedIndex=1;
modeEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-output-mode").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
var dynamicsPreset=Preset();Check(dynamicsPreset.Dynamics![0].LoadMode==1 && dynamicsPreset.Dynamics[1].LoadMode==0 && !dynamicsPreset.Dynamics[0].Bridged,"Mode edits affect one output without switching bridge");
Click("◖   Outputs");
Check(Math.Abs(Tagged<Border>("output-trim-A").Bounds.Height-76)<.1 && Math.Abs(Tagged<ToggleButton>("mute-editor-A").Bounds.Height-76)<.1 && Math.Abs(Tagged<ToggleButton>("mute-editor-A").Bounds.Width-Tagged<Button>("output-mode-A").Bounds.Width)<1.1,"Trim and symbol-only mute align with the output blocks");
Press(Tagged<Button>("output-bridge-A"));var bridgeEditor=window.OwnedWindows.Single();Press(Tagged<Button>("output-bridge-B"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(bridgeEditor,window.OwnedWindows.Single()),"A/B share one stereo/bridge editor");
bridgeEditor.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.Tag?.ToString()=="output-bridge-choice").SelectedIndex=1;
bridgeEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-output-bridge").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
var bridgePreset=Preset();Check(bridgePreset.Dynamics![0].Bridged && bridgePreset.Dynamics[1].Bridged && !bridgePreset.Dynamics[2].Bridged && bridgePreset.Dynamics[0].LoadMode==1,"Bridge updates A/B together, preserves C/D and retains load mode");
Click("◖   Outputs");Press(Tagged<Button>("output-bridge-D"));bridgeEditor=window.OwnedWindows.Single();
bridgeEditor.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.Tag?.ToString()=="output-bridge-choice").SelectedIndex=1;
bridgeEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-output-bridge").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().Dynamics!.All(d=>d.Bridged),"C/D stereo/bridge editor operates its own pair");
Click("◖   Outputs");
Tagged<ToggleButton>("mute-editor-A").IsChecked=true;Layout();
Check(Preset().Outputs[0].Muted,"Styled output mute still commits immediately");
Click("◖   Outputs");Tagged<ToggleButton>("mute-editor-A").IsChecked=false;Layout();
Check(!Preset().Outputs[0].Muted,"Styled output mute can unmute again");
Click("◖   Outputs");Press(Tagged<Button>("output-noise-A"));var noiseEditor=window.OwnedWindows.Single();
noiseEditor.GetLogicalDescendants().OfType<CheckBox>().Single(b=>b.Tag?.ToString()=="noise-gate-enabled").IsChecked=true;
noiseEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(b=>b.Tag?.ToString()=="noise-gate-db").Value=-65;
noiseEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-noise-gate").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().Dynamics![0] is {NoiseGateEnabled:true,NoiseGateDb:-65} && !Preset().Dynamics![1].NoiseGateEnabled,"Noise gate on/off and dB edit are per output");
Click("◖   Outputs");Press(Tagged<Button>("output-limiters-A"));var limiterEditor=window.OwnedWindows.Single();Press(Tagged<Button>("output-limiters-A"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(limiterEditor,window.OwnedWindows.Single()),"Repeated limiter clicks reuse the existing editor");
void SetLimiter(string tag,decimal value)=>limiterEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()==tag).Value=value;
SetLimiter("rms-threshold",60);SetLimiter("rms-attack",40);SetLimiter("rms-release",8);SetLimiter("peak-threshold",100);SetLimiter("peak-hold",80);SetLimiter("peak-release",450);
limiterEditor.GetLogicalDescendants().OfType<CheckBox>().Single(c=>c.Tag?.ToString()=="rms-auto").IsChecked=true;
Check(!limiterEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="rms-attack").IsEnabled,"Automatic RMS timing disables manual timing controls");
using(var rendered=new RenderTargetBitmap(new PixelSize(760,590))){limiterEditor.Measure(new Size(760,590));limiterEditor.Arrange(new Rect(0,0,760,590));limiterEditor.UpdateLayout();rendered.Render(limiterEditor);rendered.Save("artifacts/output-limiters.png",PngBitmapEncoderOptions.Default);}
limiterEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-limiters").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
var appliedDynamics=Preset().Dynamics![0];Check(appliedDynamics.Rms is {ThresholdVolts:60,AttackMs:40,ReleaseMultiplier:8,Automatic:true} && appliedDynamics.Peak is {ThresholdVolts:100,HoldMs:80,ReleaseMs:450},"Peak and RMS limiter editors commit every displayed setting");
Click("◖   Outputs");Save("outputs-dynamics");
Check(!Text("Unavailable"),"Outputs contains functional mode, noise-gate and limiter controls");
Device("demo-2002"); Click("⌂   Main");
Check(All<Slider>().Count() == 2 && All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("telemetry-") == true) == 8, "Two-output model has independent measurements");
Check(Tagged<Border>("input-channel-A").Bounds.Size == fourChannelSize && Tagged<Border>("output-channel-A").Bounds.Size == fourChannelSize, "Two- and four-channel models retain identical group dimensions");
Save("main-2002"); Save("main-small", 1080, 680);
Click("◉   Inputs"); Check(All<RoutingDial>().Count() == 4, "DSP-2002 exposes the same two-channel matrix features");
Device("demo-1502");
Check(All<RoutingDial>().Count() == 4, "DSP-1502 has four editable circles"); Save("routing-1502");
Check(!All<Button>().Any(b=>b.Content?.ToString() is "≋   EQ" or "∿   FIR" or "⌁   Network"), "EQ, FIR and Network tabs are removed");
var presetArgument = Array.IndexOf(args, "--preset");
if (presetArgument >= 0)
{
    Device("demo-1004");
    window.LoadPresetFile(Path.GetFileName(args[presetArgument+1]),File.ReadAllBytes(args[presetArgument+1]));
    Layout(); Save("native-preset"); Check(Preset().OutputProcessing?.Count==4, "Native preset loads full output processing");
}
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
Check(Text("Delay") && Text("EQ") && Text("Volume A"), "Routing environment includes editable processing and output gain");
Press(Tagged<Button>("input-delay-1"));
var firstDelayWindow = window.OwnedWindows.Single(); Press(Tagged<Button>("input-delay-1"));
Check(window.OwnedWindows.Count == 1 && ReferenceEquals(firstDelayWindow, window.OwnedWindows.Single()), "Repeated delay clicks activate the existing editor");
var delayEditor = window.OwnedWindows.Single();
var delayMs = delayEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => n.Tag?.ToString() == "delay-ms");
var delayMeters = delayEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => n.Tag?.ToString() == "delay-meters");
delayMeters.Value = 3.43m;
Check(Math.Abs((double)delayMs.Value! - 10) < .0001, "Metres and milliseconds stay synchronized");
delayEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-delay").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
Check(Preset().Inputs![0].DelayMs == 10, "Delay editor commits through the shared input service");
Click("◉   Inputs"); Press(Tagged<Button>("input-eq-1"));
var eqEditor = window.OwnedWindows.Single();
Press(Tagged<Button>("input-eq-1")); Check(window.OwnedWindows.Count==1 && ReferenceEquals(eqEditor,window.OwnedWindows.Single()), "Repeated EQ clicks activate the existing editor");
var graph=eqEditor.GetLogicalDescendants().OfType<EqualizerGraph>().Single();
graph.MoveBand(1,1000,6);
var hpType=eqEditor.GetLogicalDescendants().OfType<ComboBox>().Single(n=>n.Tag?.ToString()=="hp-type");
var lpType=eqEditor.GetLogicalDescendants().OfType<ComboBox>().Single(n=>n.Tag?.ToString()=="lp-type");
Check(hpType.Items.Count==11 && lpType.Items.Count==11,"HP and LP offer all eleven reference filter choices");
hpType.SelectedItem=CrossoverFilters.Options.Single(o=>o.ShortName=="LR-24");lpType.SelectedItem=CrossoverFilters.Options.Single(o=>o.ShortName=="BE-48");
eqEditor.GetLogicalDescendants().OfType<CheckBox>().Single(n=>n.Tag?.ToString()=="hp-enabled").IsChecked=true;
eqEditor.GetLogicalDescendants().OfType<CheckBox>().Single(n=>n.Tag?.ToString()=="lp-enabled").IsChecked=true;
graph.MoveCrossover(true,80);graph.MoveCrossover(false,16000);
Check(eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="hp-frequency").Value==80 && eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="lp-frequency").Value==16000,"Crossover graph handles synchronize cutoff fields");
Check(eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="eq-frequency-1").Value==1000 && eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="eq-gain-1").Value==6,"Graph handles synchronize numeric EQ draft fields");
using(var renderedEq = new RenderTargetBitmap(new PixelSize(1400,800))) { eqEditor.Measure(new Size(1400,800));eqEditor.Arrange(new Rect(0,0,1400,800));eqEditor.UpdateLayout();renderedEq.Render(eqEditor);renderedEq.Save("artifacts/input-eq-graph.png",PngBitmapEncoderOptions.Default); }
eqEditor.GetLogicalDescendants().OfType<CheckBox>().Single(b=>b.Tag?.ToString()=="input-eq-enabled").IsChecked=false;
eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(b=>b.Tag?.ToString()=="eq-gain-1").Value=3;
eqEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-input-eq").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
var processed=Preset().Inputs![0];
Check(!processed.EqEnabled && processed.Bands[0].GainDb==3 && processed.HighPass is {FrequencyHz:80,FilterType:6,Enabled:true} && processed.LowPass is {FrequencyHz:16000,FilterType:9,Enabled:true},"Input EQ applies bands, HP and LP through the shared service");
Click("◉   Inputs");
Tagged<GainReadout>("routing-output-gain-A").Value=-4;
Check(Preset().Outputs[0].GainDb == -4, "Inputs output gain is editable and persists");
Click("◉   Inputs");
Check(!All<TextBlock>().Any(t=>t.Text is "Output A" or "Output B" or "Output C" or "Output D") && Tagged<Control>("routing-environment").Bounds.Width < 1250, "Compact matrix removes left output labels and unused column spacing");
Save("routing-editable-inputs");
Press(Tagged<Button>("software-updates")); var updateDialog=window.OwnedWindows.Single(); Press(Tagged<Button>("software-updates"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(updateDialog,window.OwnedWindows.Single()), "Only one software update dialog is opened");
Check(updateDialog.GetLogicalDescendants().OfType<Button>().Any(b=>b.Tag?.ToString()=="check-for-updates") && !updateDialog.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="install-update").IsEnabled,"Updater starts with an explicit check and no implicit download"); updateDialog.Close();
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
