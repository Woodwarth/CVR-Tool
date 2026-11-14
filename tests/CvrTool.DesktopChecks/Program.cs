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
var demoService = new AmplifierService(new DemoBackend()); demoService.AddDemo("DSP-2002");
var window = new MainWindow(demoService); window.Show();
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
void WaitUi(Func<bool> completed)
{
    var timeout=System.Diagnostics.Stopwatch.StartNew();
    while (!completed() && timeout.Elapsed < TimeSpan.FromSeconds(4)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    Layout();
    if(!completed()) throw new Exception("Asynchronous UI action did not complete.");
}
void Click(string content) => Press(Button(content));
void ConfirmBridge(string tag)
{
    Press(Tagged<Button>(tag));
    var confirm = window.OwnedWindows.Single(w => w.Title!.StartsWith("Switch to "));
    confirm.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "confirm-preset-operation").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
}

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
var application = (App)Avalonia.Application.Current!;
var originalVariant = application.RequestedThemeVariant;
application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
Layout(); Save("main-light");
var lightInk = ((ISolidColorBrush)window.Foreground!).Color;
application.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
Layout();
Check(lightInk != ((ISolidColorBrush)window.Foreground!).Color, "Open desktop changes palette when the application theme switches");
Layout(); Save("main-1004");
// Browser status remains visible outside the pairing dialog.
var browserStatusServer=(CvrTool.Web.BrowserControlServer)typeof(MainWindow).GetField("browserServer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
Check(Avalonia.Automation.AutomationProperties.GetName(Tagged<Button>("browser-control"))=="Browser control · Off","Header shows stopped browser server");
browserStatusServer.StartAsync(System.Net.IPAddress.Loopback,0).GetAwaiter().GetResult(); Layout();
Check(Avalonia.Automation.AutomationProperties.GetName(Tagged<Button>("browser-control"))=="Browser control · On","Starting browser server updates the persistent header indicator");
using(var browserStatusClient=new System.Net.Http.HttpClient())
{
    browserStatusClient.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",browserStatusServer.AccessKey);
    browserStatusClient.DefaultRequestHeaders.Add("X-CVR-Client",new string('d',32));
    browserStatusClient.GetAsync(browserStatusServer.Address+"/api/health").GetAwaiter().GetResult().EnsureSuccessStatusCode(); Layout();
    Check(Avalonia.Automation.AutomationProperties.GetName(Tagged<Button>("browser-control"))=="Browser control · Connected","Connected browser is visible in the app header");
    Press(Tagged<Button>("browser-control"));
    var browserStatusWindow=window.OwnedWindows.Single();
    Check(browserStatusWindow.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("1/1 browser connected")==true),"Pairing dialog shows the single occupied browser slot");
    browserStatusWindow.Close(); Layout(); Save("main-web-active");
    Check(Avalonia.Automation.AutomationProperties.GetName(Tagged<Button>("browser-control"))=="Browser control · Connected","Closing the pairing dialog keeps the active indicator visible");
    browserStatusClient.PostAsync(browserStatusServer.Address+"/api/disconnect",null).GetAwaiter().GetResult(); Layout();
    Check(Avalonia.Automation.AutomationProperties.GetName(Tagged<Button>("browser-control"))=="Browser control · On","Browser disconnect leaves the server active and waiting");
}
browserStatusServer.DisposeAsync().AsTask().GetAwaiter().GetResult(); Layout();
Check(Avalonia.Automation.AutomationProperties.GetName(Tagged<Button>("browser-control"))=="Browser control · Off","Stopping the server clears the active indicator");

var fourChannelSize = Tagged<Border>("input-channel-A").Bounds.Size;
Check(window.Icon is not null, "Window icon loads from embedded application assets");
Check(All<TextBlock>().Count(t => t.Tag?.ToString()?.StartsWith("info-") == true) == 5, "Compact UI identity header keeps source documentation out of the interface");
Check(!Text("Changes apply immediately") && !Text("No live measurements"), "Main section footer hints are removed");
Check(!All<GainReadout>().Any() && !All<TextBox>().Any() && Tagged<TextBlock>("gain-A").Text == "0.0 dB", "Main gains are plain text without textbox borders or edit controls");
var mainSlider = All<Slider>().First();
mainSlider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up }); Layout();
Check(Preset().Inputs![0].GainDb == .5 && Preset().Outputs[0].GainDb==0, "Main arrow-up changes gain by 0.5 dB");
Click("⌂   Main"); mainSlider = All<Slider>().First();
mainSlider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down }); Layout();
Check(Preset().Inputs![0].GainDb == 0, "Main arrow-down changes gain by 0.5 dB"); Click("⌂   Main");
Check(All<Slider>().Count() == 4, "Four channel faders on Main");
Check(All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("channel-meter-") == true) == 4 && All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("channel-clip-") == true) == 4, "Every channel fader has a meter and clip status");
Check(All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("telemetry-") == true) == 16, "Four measurements per output");
Check(All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("output-status-") == true) == 36, "Nine independent status flags per output");
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
Check(Preset().Inputs![0].GainDb == -6 && Preset().Outputs[0].GainDb==0, "Numeric input gain applies immediately and exports");
Click("⌂   Main"); All<Slider>().First().Value = -9; Tagged<ToggleButton>("mute-output-A").IsChecked = false;
Check(Tagged<ToggleButton>("mute-output-A").Background is ISolidColorBrush neutral && neutral.Color == Color.Parse("#182129") && Tagged<ToggleButton>("mute-output-A").BorderBrush is ISolidColorBrush neutralBorder && neutralBorder.Color == Color.Parse("#46525D"), "Unmuted button uses neutral colors without a red border");
var preset = Preset(); Check(preset.Inputs![0].GainDb==-9 && preset.Outputs[0] is { GainDb: 0, Muted: false }, "Slider gain applies immediately and mute preserves it");
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
var typedDial = Tagged<RoutingDial>("route-dial-2-1");
typedDial.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "-12,5" });
typedDial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
Check(demoService.GetRouting("demo-1004").Cells.Single(c => c.Input == 2 && c.Output == 1).GainDb == -12.5, "Typed matrix gain accepts a decimal comma and commits with Enter");
typedDial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
Check(typedDial.GainDb == -12, "Routing arrows adjust gain in half-decibel steps");
typedDial.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
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
Device("demo-2002"); Device("demo-1004");
Check(Tagged<Button>("device-name-display").Content is TextBlock retained && retained.Text == "Front amplifier" && Preset().Name == "Front amplifier", "Renamed device survives switching and exports");
Check(Tagged<ToggleButton>("device-wink").IsEnabled && !Tagged<ToggleButton>("device-wink").GetLogicalDescendants().OfType<TextBlock>().Any(), "Wink is a symbol-only header control beside lock and standby");
try { window.LoadPresetFile("not-native.json", System.Text.Encoding.UTF8.GetBytes(window.ExportSelectedPreset())); throw new Exception("JSON file accepted"); }
catch (ArgumentException) { Check(true, "Desktop preset loading rejects JSON files"); }
Check(Tagged<ToggleButton>("device-lock").IsEnabled && Tagged<ToggleButton>("device-standby").IsEnabled && !Tagged<ToggleButton>("device-lock").GetLogicalDescendants().OfType<TextBlock>().Any(), "Lock and standby are enabled symbol-only controls");
Tagged<ToggleButton>("device-lock").IsChecked = true; Tagged<ToggleButton>("device-standby").IsChecked = true;
Device("demo-2002"); Check(Tagged<ToggleButton>("device-lock").IsChecked == false && Tagged<ToggleButton>("device-standby").IsChecked == false, "Demo lock and standby isolate devices");
Device("demo-1004"); Check(Tagged<ToggleButton>("device-lock").IsChecked == true && Tagged<ToggleButton>("device-standby").IsChecked == true, "Demo lock and standby survive device switching");
Tagged<ToggleButton>("device-lock").IsChecked = false; Tagged<ToggleButton>("device-standby").IsChecked = false;
Click("◖   Outputs"); Save("outputs");
Press(Tagged<Button>("output-delay-A"));
var outputDelay=window.OwnedWindows.Single();
outputDelay.GetLogicalDescendants().OfType<TextBox>().Single(n=>n.Tag?.ToString()=="delay-ms").Text="4.5";
outputDelay.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-delay").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Layout();
Check(Preset().Outputs[0].DelayMs == 4.5, "Output delay applies through shared service");
Click("◖   Outputs");
Press(Tagged<Button>("output-eq-A"));var outputEq=window.OwnedWindows.Single();Press(Tagged<Button>("output-eq-A"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(outputEq,window.OwnedWindows.Single()),"Output EQ reuses its channel editor");
Check(outputEq.GetLogicalDescendants().OfType<ComboBox>().Count(c=>c.Tag?.ToString() is "hp-type" or "lp-type")==2,"Output EQ includes HP and LP controls");
var eqView = outputEq.GetLogicalDescendants().OfType<EqualizerGraph>().Single();
eqView.MoveBand(2,2000,-5);
Press(outputEq.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag,"eq-reset")));
Check(eqView.Bands.All(b=>b.GainDb==0) && eqView.Bands[1].FrequencyHz==132 && eqView.HighPass?.Enabled==false && eqView.LowPass?.Enabled==false,
    "EQ reset restores the flat channel draft and bypasses crossovers");
eqView.MoveBand(2,2000,-5);
void CopyPair(Window source)
{
    Press(source.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag,"editor-copy")));
    var confirm = window.OwnedWindows.Single(w => w.Title == "Copy settings");
    Press(confirm.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag,"confirm-preset-operation")));
}
CopyPair(outputEq);
WaitUi(()=>demoService.GetOutputProcessing("demo-1004",2).Bands[1].GainDb == -5);
Check(demoService.GetOutputProcessing("demo-1004",1).Bands[1].GainDb == 0,"EQ copy commits the displayed draft to B only");
outputEq.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-input-eq").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().OutputProcessing![0].Bands[1].GainDb==-5 && Preset().Inputs![0].Bands[1].GainDb==0,"Output EQ applies independently of input EQ");
Click("◖   Outputs");
Press(Tagged<Button>("output-limiters-A"));
window.OwnedWindows.Single().GetLogicalDescendants().OfType<ComboBox>().Single(b=>Equals(b.Tag,"editor-channel")).SelectedIndex=2;
Layout();
var limiterC = window.OwnedWindows.Single();
using (var preview = new RenderTargetBitmap(new PixelSize(820,580)))
{
    limiterC.Measure(new Size(820,580)); limiterC.Arrange(new Rect(0,0,820,580)); limiterC.UpdateLayout();
    preview.Render(limiterC); preview.Save("artifacts/limiter-panel.png",PngBitmapEncoderOptions.Default);
}
NumericUpDown LimiterValue(string tag) => limiterC.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>Equals(n.Tag,tag));
LimiterValue("limiter-load").Value=8;
LimiterValue("rms-threshold").Value=80;
Check(LimiterValue("rms-power").Value==800,"80 Vrms at 8 ohms shows 800 W");
LimiterValue("peak-power").Value=1600;
Check(LimiterValue("peak-threshold").Value==113.14m,"1600 W peak at 8 ohms sets 113.14 Vp");
limiterC.GetLogicalDescendants().OfType<NumericUpDown>().Single(b=>Equals(b.Tag,"peak-threshold")).Value=11;
CopyPair(limiterC);
WaitUi(()=>demoService.GetOutputDynamics("demo-1004",4).Peak.ThresholdVolts==11);
Check(demoService.GetOutputDynamics("demo-1004",3).Peak.ThresholdVolts!=11,"Limiter channel switch and C to D draft copy preserve C");
LimiterValue("peak-threshold").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
Layout();
Check(window.OwnedWindows.Count == 0, "Escape from a limiter field closes the editor");
Press(Tagged<Button>("output-fir-A"));var firWindow=window.OwnedWindows.Single();Press(Tagged<Button>("output-fir-A"));
Check(window.OwnedWindows.Count==1 && firWindow.GetLogicalDescendants().OfType<ResponsePlot>().Count()==3,"FIR has one editor with impulse, frequency and phase plots");
Check(firWindow.GetLogicalDescendants().OfType<Button>().Count(b=>b.Tag?.ToString() is "fir-import" or "fir-export" or "fir-remove")==3,"FIR exposes import, export and remove controls");
firWindow.GetLogicalDescendants().OfType<ToggleButton>().Single(b=>b.Tag?.ToString()=="fir-enabled").IsChecked=true;
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
Press(Tagged<Button>("output-trim-A"));
Check(Tagged<GainReadout>("output-gain-A").GetLogicalDescendants().OfType<TextBox>().Any(), "Clicking the entire Trim tile opens its editor");
var tileTrimEditor=Tagged<GainReadout>("output-gain-A").GetLogicalDescendants().OfType<TextBox>().Single();
tileTrimEditor.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.Escape }); Layout();
Check(Tagged<Button>("output-bridge-A").GetLogicalDescendants().OfType<TextBlock>().Single().Text=="Stereo", "Bridge tile displays only the current Stereo/Bridge state");
Check(Math.Abs(Tagged<Button>("output-trim-A").Bounds.Height-76)<.1 && Math.Abs(Tagged<ToggleButton>("mute-editor-A").Bounds.Height-76)<.1 && Math.Abs(Tagged<ToggleButton>("mute-editor-A").Bounds.Width-Tagged<Button>("output-mode-A").Bounds.Width)<1.1,"Trim and symbol-only mute align with the output blocks");
Press(Tagged<Button>("output-mode-B"));Check(window.OwnedWindows.Count==1,"Secondary output editor can be opened in stereo");
Press(Tagged<Button>("output-bridge-A"));
var bridgePrompt=window.OwnedWindows.Single(w=>w.Title!.StartsWith("Switch to "));
Check(!JsonSerializer.Deserialize<PresetDocument>(window.ExportSelectedPreset())!.Dynamics![0].Bridged, "Bridge does not change before explicit confirmation");
bridgePrompt.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="cancel-preset-operation").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(!JsonSerializer.Deserialize<PresetDocument>(window.ExportSelectedPreset())!.Dynamics![0].Bridged, "Cancelling bridge confirmation preserves the pair state");
ConfirmBridge("output-bridge-A");
Check(window.OwnedWindows.Count==0,"Confirmed bridge change closes the follower editor");
var bridgePreset=Preset();Check(bridgePreset.Dynamics![0].Bridged && bridgePreset.Dynamics[1].Bridged && !bridgePreset.Dynamics[2].Bridged && bridgePreset.Dynamics[0].LoadMode==1,"Bridge updates A/B together, preserves C/D and retains load mode");
Click("◖   Outputs");
bool OutputControlsEnabled(string channel)=>Tagged<Grid>("output-flow-"+channel).Children.Where(c=>Grid.GetColumn(c)<11).All(c=>c.IsEnabled && c.Opacity==1);
bool OutputControlsDisabled(string channel)=>Tagged<Grid>("output-flow-"+channel).Children.Where(c=>Grid.GetColumn(c)<11).All(c=>!c.IsEnabled && c.Opacity<.5);
Check(OutputControlsDisabled("B") && OutputControlsEnabled("A") && OutputControlsEnabled("C") && OutputControlsEnabled("D"),"Bridge greys and disables only the second output in A/B");
Check(Tagged<Button>("output-bridge-B").IsEnabled,"Follower bridge switch remains clickable");
ConfirmBridge("output-bridge-B");
Check(window.OwnedWindows.Count==0 && OutputControlsEnabled("B"),"Confirming the follower bridge switch restores stereo and its controls");
Check(!Preset().Dynamics![0].Bridged,"Direct bridge toggle restores the paired stereo state");
Click("◖   Outputs");ConfirmBridge("output-bridge-A");ConfirmBridge("output-bridge-D");
Check(OutputControlsDisabled("B") && OutputControlsDisabled("D") && OutputControlsEnabled("A") && OutputControlsEnabled("C"),"A/B and C/D independently disable their second outputs");
Check(window.OwnedWindows.Count==0,"Bridge confirmation closes after accepting the switch");Save("outputs-bridge-followers");
Check(Preset().Dynamics!.All(d=>d.Bridged),"C/D stereo/bridge switch operates its own pair");
Click("◖   Outputs");
Tagged<ToggleButton>("mute-editor-A").IsChecked=true;Layout();
Check(Preset().Outputs[0].Muted,"Styled output mute still commits immediately");
Click("◖   Outputs");Tagged<ToggleButton>("mute-editor-A").IsChecked=false;Layout();
Check(!Preset().Outputs[0].Muted,"Styled output mute can unmute again");
Click("◖   Outputs");Press(Tagged<Button>("output-noise-A"));var noiseEditor=window.OwnedWindows.Single();
noiseEditor.GetLogicalDescendants().OfType<ToggleButton>().Single(b=>b.Tag?.ToString()=="noise-gate-enabled").IsChecked=true;
noiseEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(b=>b.Tag?.ToString()=="noise-gate-db").Value=-65;
noiseEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-noise-gate").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
Check(Preset().Dynamics![0] is {NoiseGateEnabled:true,NoiseGateDb:-65} && !Preset().Dynamics![1].NoiseGateEnabled,"Noise gate on/off and dB edit are per output");
Click("◖   Outputs");Press(Tagged<Button>("output-limiters-A"));var limiterEditor=window.OwnedWindows.Single();Press(Tagged<Button>("output-limiters-A"));
Check(window.OwnedWindows.Count==1 && ReferenceEquals(limiterEditor,window.OwnedWindows.Single()),"Repeated limiter clicks reuse the existing editor");
void SetLimiter(string tag,decimal value)=>limiterEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()==tag).Value=value;
SetLimiter("rms-threshold",60);SetLimiter("rms-attack",40);SetLimiter("rms-release",8);SetLimiter("peak-threshold",100);SetLimiter("peak-hold",80);SetLimiter("peak-release",450);
limiterEditor.GetLogicalDescendants().OfType<ToggleButton>().Single(c=>c.Tag?.ToString()=="rms-auto").IsChecked=true;
Check(!limiterEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="rms-attack").IsEnabled,"Automatic RMS timing disables manual timing controls");
using(var rendered=new RenderTargetBitmap(new PixelSize(820,580))){limiterEditor.Measure(new Size(820,580));limiterEditor.Arrange(new Rect(0,0,820,580));limiterEditor.UpdateLayout();rendered.Render(limiterEditor);rendered.Save("artifacts/output-limiters.png",PngBitmapEncoderOptions.Default);}
limiterEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-limiters").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));Layout();
var appliedDynamics=Preset().Dynamics![0];Check(appliedDynamics.Rms is {ThresholdVolts:60,AttackMs:40,ReleaseMultiplier:8,Automatic:true} && appliedDynamics.Peak is {ThresholdVolts:100,HoldMs:80,ReleaseMs:450},"Peak and RMS limiter editors commit every displayed setting");
Click("◖   Outputs");Save("outputs-dynamics");
Check(!Text("Unavailable"),"Outputs contains functional mode, noise-gate and limiter controls");
Device("demo-2002"); Click("⌂   Main");
Check(All<Slider>().Count() == 2 && All<Control>().Count(c => c.Tag?.ToString()?.StartsWith("telemetry-") == true) == 8, "Two-output model has independent measurements");
Check(Tagged<Border>("input-channel-A").Bounds.Size == fourChannelSize && Tagged<Border>("output-channel-A").Bounds.Size == fourChannelSize, "Two- and four-channel models retain identical group dimensions");
Save("main-2002"); Save("main-small", 1080, 680);
Click("◉   Inputs"); Check(All<RoutingDial>().Count() == 4, "DSP-2002 exposes the same two-channel matrix features");
Device("demo-2002");
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
    var corner = canvas.TranslatePoint(new Point(canvas.Bounds.Width,canvas.Bounds.Height),root)!.Value;
    Check(Math.Abs(corner.X-width) <= 1 && Math.Abs(corner.Y-root.Bounds.Height) <= 1 && Math.Abs(origin.X) <= 1 && Math.Abs(origin.Y) <= 1 && firstGroup.Bounds.Size == fourChannelSize &&
        startPoint.X >= 0 && endPoint.X <= width + 1 && endPoint.Y <= height + 1 &&
        Math.Abs((corner.X-origin.X)/canvas.Bounds.Width-(corner.Y-origin.Y)/canvas.Bounds.Height) < .001,
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
var delayMs = delayEditor.GetLogicalDescendants().OfType<TextBox>().Single(n => n.Tag?.ToString() == "delay-ms");
var delayMeters = delayEditor.GetLogicalDescendants().OfType<TextBox>().Single(n => n.Tag?.ToString() == "delay-meters");
delayMs.Text = "1.5";
Check(delayMeters.Text=="0.5145", "Typing milliseconds immediately computes metres");
delayMeters.Text = "1,5";
Check(Math.Abs(double.Parse(delayMs.Text!,System.Globalization.CultureInfo.InvariantCulture)-1500.0/343)<.000001, "Typing comma-decimal metres immediately computes milliseconds");
delayMs.Text="101";
Check(!delayEditor.GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="apply-delay").IsEnabled, "Invalid delay disables apply instead of using an old value");
delayMeters.Text = "3.43";
Check(Math.Abs(double.Parse(delayMs.Text!,System.Globalization.CultureInfo.InvariantCulture) - 10) < .0001, "Metres and milliseconds stay synchronized");
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
eqEditor.GetLogicalDescendants().OfType<ToggleButton>().Single(n=>n.Tag?.ToString()=="hp-enabled").IsChecked=true;
eqEditor.GetLogicalDescendants().OfType<ToggleButton>().Single(n=>n.Tag?.ToString()=="lp-enabled").IsChecked=true;
graph.MoveCrossover(true,80);graph.MoveCrossover(false,16000);
Check(eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="hp-frequency").Value==80 && eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="lp-frequency").Value==16000,"Crossover graph handles synchronize cutoff fields");
Check(eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="eq-frequency-1").Value==1000 && eqEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(n=>n.Tag?.ToString()=="eq-gain-1").Value==6,"Graph handles synchronize numeric EQ draft fields");
using(var renderedEq = new RenderTargetBitmap(new PixelSize(1400,800))) { eqEditor.Measure(new Size(1400,800));eqEditor.Arrange(new Rect(0,0,1400,800));eqEditor.UpdateLayout();renderedEq.Render(eqEditor);renderedEq.Save("artifacts/input-eq-graph.png",PngBitmapEncoderOptions.Default); }
eqEditor.GetLogicalDescendants().OfType<ToggleButton>().Single(b=>b.Tag?.ToString()=="input-eq-enabled").IsChecked=false;
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
var pairingQr = new BrowserConnectionQrCode { Width=280, Height=280 };
const string pairingTestUrl="http://127.0.0.1:8787/#0123456789abcdef0123456789abcdef";
Check(!pairingQr.IsVisible,"Pairing QR starts hidden until browser control has a connection URL");
pairingQr.SetConnectionUrl(pairingTestUrl);
pairingQr.Measure(new Size(280,280)); pairingQr.Arrange(new Rect(0,0,280,280));
using(var pairingImage=new RenderTargetBitmap(new PixelSize(280,280))) {pairingImage.Render(pairingQr);pairingImage.Save("artifacts/browser-connection-qr.png",PngBitmapEncoderOptions.Default);}
Check(pairingQr.IsVisible && pairingQr.ConnectionUrl==pairingTestUrl,"Pairing QR contains the full connection link including its private key");
pairingQr.SetConnectionUrl(null);
Check(!pairingQr.IsVisible && pairingQr.ConnectionUrl is null,"Stopping browser control clears the old QR payload");
application.RequestedThemeVariant = originalVariant; window.Close();
var nativeService = new AmplifierService(new DemoBackend());
var nativeFixture = nativeService.ExportNativePreset("demo-1004");
System.Text.Encoding.ASCII.GetBytes("Input annotation").CopyTo(nativeFixture, 36 + 378);
System.Text.Encoding.ASCII.GetBytes("Speaker label").CopyTo(nativeFixture, 36 + 394);
nativeService.LoadPresetFile("demo-1004", "fixture.sd", nativeFixture[..2687]);
window = new MainWindow(nativeService); window.Show(); root = (Control)window.Content!; Layout();
Click("☰   Presets");
Check(!All<Control>().Any(c => Equals(c.Tag, "native-preset-information")), "Presets shows the original controls without a metadata section");
Click("◖   Outputs"); Check(Tagged<Button>("output-speaker-A").GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Speaker label"), "Output tile displays the saved speaker label");
Press(Tagged<Button>("output-fir-A")); var unknownFirViewer = window.OwnedWindows.Single();
Check(unknownFirViewer.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("FIR coefficients unknown") == true) && !unknownFirViewer.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "fir-export").IsEnabled, "Sync-only preset FIR opens without fake coefficients or export");
unknownFirViewer.Close(); window.Close();
window = new MainWindow(new AmplifierService(new TelemetryFixture())); window.Show(); root = (Control)window.Content!; Layout();
var clipped = Tagged<Control>("channel-clip-A").GetLogicalDescendants().OfType<TextBlock>().ToArray();
var notClipped = Tagged<Control>("channel-clip-B").GetLogicalDescendants().OfType<TextBlock>().ToArray();
Check(clipped.All(t => t.Foreground is ISolidColorBrush b && b.Color == Color.Parse("#FF566A")) && notClipped.All(t => t.Foreground is ISolidColorBrush b && b.Color != Color.Parse("#FF566A")), "Clip indicator and label turn red only on clipping");
Check(Tagged<Control>("channel-clip-A").GetLogicalDescendants().OfType<StatusLed>().Single().Value == true && Tagged<Control>("channel-clip-B").GetLogicalDescendants().OfType<StatusLed>().Single().Value == false && Tagged<Control>("channel-clip-C").GetLogicalDescendants().OfType<StatusLed>().Single().Value is null && All<StatusLed>().Count() == 40, "Clipping and all output status bits use LEDs with distinct on/off/unknown states");
Check(Tagged<Control>("telemetry-A-0") is TextBlock a && a.Text == "42.0" && Tagged<Control>("telemetry-B-0") is TextBlock b && b.Text == "21.0", "Each output displays its own telemetry");
Save("main-clipping-test-fixture"); window.Close();
var liveFixture = new LiveDesktopFixture();
window = new MainWindow(new AmplifierService(liveFixture)); window.Show(); root = (Control)window.Content!; Layout();
Check(!All<Button>().Any(b => b.Content?.ToString() is "Manual connection" or "Connect device" or "Disconnect"), "Automatic device view has no manual connection/disconnect buttons");
Device(LiveDesktopFixture.LiveId); Layout();
Check(All<Button>().Count(b => b.Tag?.ToString()?.StartsWith("demo-") == true) == 2, "Both demos remain listed beside the live device");
Check(Tagged<TextBlock>("device-dot-" + LiveDesktopFixture.LiveId).Foreground is ISolidColorBrush liveGreen && liveGreen.Color == Color.Parse("#00CE63"), "Connected live device point is green");
Check(Tagged<ToggleButton>("device-wink").IsEffectivelyEnabled, "Live Wink control follows evidenced capability");
Check(Tagged<ToggleButton>("device-lock").IsEffectivelyEnabled && Tagged<ToggleButton>("device-standby").IsEffectivelyEnabled && Tagged<Button>("device-name-display").IsEffectivelyEnabled, "Live header controls and verified device rename are enabled");
Check(All<Slider>().All(s => s.IsEnabled) && Tagged<ToggleButton>("mute-output-A").IsEnabled && Tagged<ToggleButton>("mute-input-A").IsEnabled, "Live Main exposes verified input gain and independent input/output mutes");
var liveFader=Tagged<Slider>("input-gain-A");
liveFader.Focus();
liveFixture.TestVoltage=41;
var refreshTask=window.RefreshLiveAsync(); WaitUi(()=>refreshTask.IsCompleted); refreshTask.GetAwaiter().GetResult();
WaitUi(()=>Tagged<TextBlock>("telemetry-A-0").Text == 41d.ToString("0.0"));
Check(ReferenceEquals(liveFader,Tagged<Slider>("input-gain-A")) && liveFader.IsFocused,
    "New live telemetry updates the reading without replacing the focused fader");
liveFader.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.Up }); Layout();
Check(liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State.GainDb==0, "Live fader previews locally before key release commits the value");
liveFader.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyUpEvent, Key=Key.Up });
WaitUi(()=>liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State.GainDb==.5 && Tagged<Slider>("input-gain-A").IsEffectivelyEnabled);
Check(Tagged<TextBlock>("gain-A").Text=="0.5 dB", "Live Main commits half-dB input gain and shows authoritative readback");
Tagged<ToggleButton>("mute-input-A").IsChecked=true;
WaitUi(()=>liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State.Muted==true && Tagged<ToggleButton>("mute-input-A").IsEffectivelyEnabled);
Check(Tagged<ToggleButton>("mute-input-A").IsChecked==true, "Live Main input mute commits through the shared application service");
Press(Tagged<Button>("device-name-display")); Tagged<TextBox>("device-name").Text="Renamed live fixture"; Press(Tagged<Button>("save-name"));
WaitUi(()=>liveFixture.LiveName=="Renamed live fixture" && Tagged<Button>("device-name-display").IsEffectivelyEnabled);
Check(Tagged<Button>("device-name-display").GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text=="Renamed live fixture"), "Live Main device-name edit shows confirmed identity state");
Check(Tagged<Control>("channel-clip-A").GetLogicalDescendants().OfType<StatusLed>().Single().Value is null, "Input clip does not copy output clip on live devices");
Click("◉   Inputs");
Check(All<RoutingDial>().All(d => d.IsEnabled && d.CanEditGain) && Tagged<Button>("input-eq-1").IsEnabled, "Live matrix exposes verified gain/mute with EQ viewable");
var liveRoute = Tagged<RoutingDial>("route-dial-1-1");
var routeBefore = liveFixture.GetRouting(LiveDesktopFixture.LiveId).Cells.Single(c => c.Input == 1 && c.Output == 1);
var pointer = new Pointer(101, PointerType.Mouse, true);
var routeOrigin = liveRoute.TranslatePoint(new Point(37,37), window)!.Value;
liveRoute.RaiseEvent(new PointerPressedEventArgs(liveRoute, pointer, window, routeOrigin, 1, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
liveRoute.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, liveRoute, pointer, window, routeOrigin + new Vector(0,-10), 2, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
Check(liveRoute.GainDb != routeBefore.GainDb && liveFixture.GetRouting(LiveDesktopFixture.LiveId).Cells.Single(c => c.Input == 1 && c.Output == 1).GainDb == routeBefore.GainDb, "Live matrix drag previews without sending intermediate gain writes");
var expectedRouteGain = liveRoute.GainDb;
liveRoute.RaiseEvent(new PointerReleasedEventArgs(liveRoute, pointer, window, routeOrigin + new Vector(0,-10), 3, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
WaitUi(() => liveFixture.GetRouting(LiveDesktopFixture.LiveId).Cells.Single(c => c.Input == 1 && c.Output == 1).GainDb == expectedRouteGain && Tagged<RoutingDial>("route-dial-1-1").IsEffectivelyEnabled);
Check(liveFixture.GetRouting(LiveDesktopFixture.LiveId).Cells.Single(c => c.Input == 1 && c.Output == 1).Muted == routeBefore.Muted, "Live route gain commits on release and preserves mute");

Press(Tagged<Button>("input-eq-1")); var liveEq = window.OwnedWindows.Single();
Check(liveEq.Title!.Contains("Live") && liveEq.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "apply-input-eq").IsEnabled && liveEq.GetLogicalDescendants().OfType<ComboBox>().Single(b => b.Tag?.ToString() == "hp-type") is { IsEnabled: true, ItemCount: 2 }, "Live EQ offers only evidenced BW-12/BW-24 crossover types");
var liveGraph = liveEq.GetLogicalDescendants().OfType<EqualizerGraph>().Single();
var originalHp = liveGraph.HighPass;
liveGraph.MoveCrossover(true, 300);
Check(liveGraph.IsEffectivelyEnabled && liveGraph.CanEditCrossovers && liveGraph.HighPass?.FrequencyHz == 300 && liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State.HighPass == originalHp, "Live HP graph adjusts a draft without sending until Apply");
liveGraph.MoveCrossover(false,18000);
var peakBandsBefore = liveGraph.Bands;
liveGraph.Bands = peakBandsBefore.Select(b => b.Number == 2 ? b with { FilterType = 1 } : b).ToArray();
var protectedBandBefore = liveGraph.Bands.Single(b => b.Number == 2);
liveGraph.MoveBand(2, 3000, 8);
Check(liveGraph.Bands.Single(b => b.Number == 2) == protectedBandBefore, "Live graph leaves unsupported band types unchanged");
liveGraph.Bands = peakBandsBefore;
liveGraph.MoveBand(1, 1200, 3.5);
Check(liveGraph.Bands.Single(b => b.Number == 1) is { FrequencyHz: 1200, GainDb: 3.5 } && liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State.Bands[0].GainDb != 3.5, "Live graphical EQ edits remain drafts until Apply");
liveEq.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag?.ToString() == "apply-input-eq").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
WaitUi(() => !window.OwnedWindows.Contains(liveEq));
Check(liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State.Bands[0] is { FrequencyHz: 1200, GainDb: 3.5 }, "Live graphical Peak edit applies through the shared service");
Check(liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1).State is { HighPass.FrequencyHz: 300, LowPass.FrequencyHz: 18000 }, "Live graphical HP/LP edits apply through the shared service");
Click("◉   Inputs");
var liveOutputGain = Tagged<GainReadout>("routing-output-gain-A");
Check(liveOutputGain.GetLogicalDescendants().OfType<Button>().Single().IsEffectivelyEnabled, "Live Inputs output gain has an enabled click target");
Press(liveOutputGain.GetLogicalDescendants().OfType<Button>().Single());
var liveGainEntry = liveOutputGain.GetLogicalDescendants().OfType<TextBox>().Single();
liveGainEntry.Text = "-6,5";
var inputEditTimer = System.Diagnostics.Stopwatch.StartNew();
while (inputEditTimer.Elapsed < TimeSpan.FromMilliseconds(1300)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
Check(ReferenceEquals(liveGainEntry, Tagged<GainReadout>("routing-output-gain-A").GetLogicalDescendants().OfType<TextBox>().Single()) && liveGainEntry.Text == "-6,5", "Automatic live refresh preserves an in-progress Inputs gain draft");
liveGainEntry.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.Enter });
WaitUi(() => liveFixture.GetChannel(LiveDesktopFixture.LiveId,1).State.GainDb == -6.5 && Tagged<GainReadout>("routing-output-gain-A").IsEffectivelyEnabled);
Check(Tagged<GainReadout>("routing-output-gain-A").Value == -6.5, "Live Inputs output gain accepts decimal comma and shows confirmed value");
var unsupportedHpOriginal = liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1);
liveFixture.ApplyInputProcessing(new(LiveDesktopFixture.LiveId,1,HighPass:unsupportedHpOriginal.State.HighPass! with {FilterType=1}),unsupportedHpOriginal.Revision);
window.ReloadSession(); Layout(); Press(Tagged<Button>("input-eq-1"));
var unsupportedHpEditor=window.OwnedWindows.Single();
Check(!unsupportedHpEditor.GetLogicalDescendants().OfType<ComboBox>().Single(c=>c.Tag?.ToString()=="hp-type").IsEffectivelyEnabled && unsupportedHpEditor.GetLogicalDescendants().OfType<NumericUpDown>().Single(c=>c.Tag?.ToString()=="lp-frequency").IsEffectivelyEnabled, "An unverified stored HP type stays protected while known LP remains editable");
var unsupportedGraph=unsupportedHpEditor.GetLogicalDescendants().OfType<EqualizerGraph>().Single();
var unsupportedFilterBefore=unsupportedGraph.HighPass;
unsupportedGraph.MoveCrossover(true,500);
Check(unsupportedGraph.HighPass==unsupportedFilterBefore, "Protected stored crossover types cannot be changed through graph handles");
unsupportedHpEditor.Close();
var unsupportedHpCurrent=liveFixture.GetInputProcessing(LiveDesktopFixture.LiveId,1);
liveFixture.ApplyInputProcessing(new(LiveDesktopFixture.LiveId,1,HighPass:unsupportedHpOriginal.State.HighPass),unsupportedHpCurrent.Revision);
window.ReloadSession(); Layout();

var heldOutputsTab = Tagged<Button>("Outputs");
window.ReloadSession(); Layout();
Check(ReferenceEquals(heldOutputsTab, Tagged<Button>("Outputs")), "Live refresh retains the tab through a press/release");
Press(heldOutputsTab); Check(Tagged<Button>("output-mode-A").IsEnabled && Tagged<Button>("output-eq-A").IsEnabled, "Live output settings can be inspected through protected editors");
foreach (var tag in new[] { "output-eq-A", "output-mode-A", "output-delay-A", "output-noise-A", "output-limiters-A", "output-fir-A" })
{
    Press(Tagged<Button>(tag)); var viewer = window.OwnedWindows.Single();
    var writesAllowed = true;
    Check(viewer.Title!.Contains(writesAllowed ? "Live" : "Read-only") && viewer.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag?.ToString()?.StartsWith("apply-") == true).All(b => b.IsEffectivelyEnabled == writesAllowed), "Live editor permits only verified writes for " + tag);
    if(tag=="output-mode-A") Check(viewer.GetLogicalDescendants().OfType<ComboBox>().Single(b=>b.Tag?.ToString()=="output-mode-choice").ItemCount==2,"Live mode offers only verified Low-Z and 70V variants");
    if (tag == "output-fir-A") Check(viewer.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("FIR coefficients unknown") == true), "Unread live FIR coefficients show unknown instead of an invented curve");
    viewer.Close();
}
Click("☰   Presets"); Check(Tagged<Button>("preset-save-pc").IsEnabled && Tagged<ComboBox>("device-preset-slot").Items.Count == 0 && Tagged<Button>("preset-factory-reset").IsEnabled, "Live presets expose the shared layout and protect unavailable actions");
liveFixture.Error = "Offline fixture"; window.ReloadSession(); Layout();
Check(Tagged<TextBlock>("device-dot-" + LiveDesktopFixture.LiveId).Foreground is ISolidColorBrush liveRed && liveRed.Color == Color.Parse("#F05462"), "Disconnected live device point turns red");
Click("⌂   Main");
Check(!Tagged<ToggleButton>("device-lock").IsEffectivelyEnabled && !Tagged<ToggleButton>("device-standby").IsEffectivelyEnabled && !Tagged<ToggleButton>("device-wink").IsEffectivelyEnabled && !Tagged<ToggleButton>("mute-output-A").IsEffectivelyEnabled, "Offline device retains readings while operational controls and mute are disabled");
liveFixture.Error = null; window.ReloadSession(); Layout();
Check(Tagged<ToggleButton>("device-wink").IsEffectivelyEnabled && Tagged<ToggleButton>("mute-output-A").IsEffectivelyEnabled, "Fresh connection restores only the supported live controls");
Device("demo-2002"); Click("⌂   Main"); Check(All<Slider>().All(s => s.IsEnabled), "Switching back to a demo restores editable controls");
window.Close(); Console.WriteLine($"PASS: {count} desktop checks");

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
        1 => t with { InputMeterDb = -1, InputClip = true, LevelDbFs = -1, Voltage = 42, Current = 5.25, LoadOhms = 8, TemperatureC = 55, Status = new OutputStatus(Clip: true) },
        2 => t with { InputMeterDb = -12, InputClip = false, LevelDbFs = -12, Voltage = 21, Current = 2.625, LoadOhms = 8, TemperatureC = 40, Status = new OutputStatus(Clip: false) },
        _ => t with { InputMeterDb = null, InputClip = null }
    }).ToArray();
}
