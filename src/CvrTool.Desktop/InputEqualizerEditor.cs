// Desktop / Input EQ editor
// Module: Provides the Avalonia interface for monitoring and controlling amplifiers.
// Purpose: combine a draggable response graph and reference-style band cards.
// Units: Hz, dB and Q; the graph explicitly previews at 48 kHz.
// State: one window per input; drafts apply through revision-bound plans.
// Scope: crossovers and device filter types are preserved, not guessed.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using CvrTool.Core;
using CvrTool.Application;

namespace CvrTool.Desktop;

public sealed partial class MainWindow
{
    private void OpenInputEqEditor(int channel, bool output=false)
    {
        var editedId = deviceId;
        var side=output ? "Output" : "Input";
        var kind=output ? "output-eq" : "eq";
        if (ActivateInputEditor(editedId, channel, kind))
            return;
        var input = (output ? service.GetOutputProcessing(editedId,channel) : service.GetInputProcessing(editedId, channel));
        var draft = input.Bands.ToArray();
        var content = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 14 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16 };
        header.Children.Add(Heading(side+" EQ · Channel " + channel));
        var enabled = new CheckBox { Content = input.EqEnabled ? "EQ On" : "EQ Off", IsChecked = input.EqEnabled, Tag = "input-eq-enabled" };
        AddColumn(header, enabled, 1);
        content.Children.Add(header);
        var graph = new EqualizerGraph { HighPass = input.HighPass, LowPass = input.LowPass, Bands = draft, EqEnabled = input.EqEnabled, Tag = output ? "output-eq-graph" : "input-eq-graph", Height = 340 };
        var legend = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        legend.Children.Add(Text("Gain (dB)", 13, Accent));
        legend.Children.Add(Text("Phase (°)", 13, Avalonia.Media.Brush.Parse("#F0BD71")));
        AddColumn(header, legend, 2);
        Grid.SetRow(graph, 1);
        content.Children.Add(graph);
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", 10))), ColumnSpacing = 8 };
        var fields = new Dictionary<int, (NumericUpDown Frequency, NumericUpDown Gain, NumericUpDown Q, CheckBox Enabled)>();
        var syncing = false;
        foreach (var band in draft)
        {
            var column = new StackPanel { Spacing = 8 };
            column.Children.Add(Centered("EQ " + band.Number, 14, Accent));
            column.Children.Add(Centered(band.FilterType == 0 ? "Peak" : "Device type " + band.FilterType, 11, Muted));
            var frequency = Number(20, 20000, 1, (decimal)band.FrequencyHz);
            frequency.FormatString = "0";
            frequency.Tag = "eq-frequency-" + band.Number;
            var gain = Number(-18, 18, .5m, (decimal)band.GainDb);
            gain.Tag = "eq-gain-" + band.Number;
            var q = Number(.1m, 20, .01m, (decimal)band.Q);
            q.Tag = "eq-q-" + band.Number;
            q.FormatString = "0.00";
            var active = new CheckBox { Content = "On", IsChecked = band.Enabled, Tag = "eq-band-enabled-" + band.Number };
            column.Children.Add(Field("Hz", frequency));
            column.Children.Add(Field("dB", gain));
            column.Children.Add(Field("Q", q));
            column.Children.Add(active);
            var card = Card(column);
            card.Padding = new Thickness(8);
            AddColumn(cards, card, band.Number);
            fields.Add(band.Number, (frequency, gain, q, active));
            void Changed()
            {
                if (syncing || frequency.Value is null || gain.Value is null || q.Value is null)
                    return;
                draft[band.Number - 1] = band with
                {
                    FrequencyHz = (double)frequency.Value.Value,
                    GainDb = (double)gain.Value.Value,
                    Q = (double)q.Value.Value,
                    Enabled = active.IsChecked == true
                };
                graph.Bands = draft;
            }
            frequency.ValueChanged += (_, _) => Changed();
            gain.ValueChanged += (_, _) => Changed();
            q.ValueChanged += (_, _) => Changed();
            active.IsCheckedChanged += (_, _) => Changed();
        }
        var highPass = input.HighPass;
        var lowPass = input.LowPass;
        var crossoverFields = new Dictionary<bool, NumericUpDown>();
        var resetCrossovers = new List<Action>();
        Control CrossoverCard(bool high)
        {
            var filter = (high ? highPass : lowPass);
            var key = high ? "hp" : "lp";
            var column = new StackPanel { Spacing = 8 };
            column.Children.Add(Centered(high ? "HP" : "LP", 14, Accent));
            var type = new ComboBox
            {
                ItemsSource = !output && service.GetDevice(editedId).Readings?.ReadOnly == true && ObservedInputCrossovers.CanWrite(filter)
                    ? ObservedInputCrossovers.Options : CrossoverFilters.Options,
                SelectedItem = CrossoverFilters.Options.SingleOrDefault(o => o.Code == filter?.FilterType),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 0,
                MaxDropDownHeight = 380,
                Tag = key + "-type",
                ItemTemplate = new FuncDataTemplate<CrossoverOption>((o, _) => new TextBlock { Text = o?.Label, FontSize = 12 }),
                SelectionBoxItemTemplate = new FuncDataTemplate<CrossoverOption>((o, _) => new TextBlock { Text = o?.ShortName, FontSize = 12 })
            };
            var frequency = Number(20, 20000, 1, (decimal)(filter?.FrequencyHz ?? (high ? 20 : 20000)));
            frequency.Tag = key + "-frequency";
            frequency.FormatString = "0";
            var active = new CheckBox { Content = "On", IsChecked = filter?.Enabled == true, Tag = key + "-enabled" };
            column.Children.Add(type);
            column.Children.Add(Field("Hz", frequency));
            column.Children.Add(active);
            column.Children.Add(Text("Cutoff", 11, Muted));
            column.IsEnabled = filter is not null;
            crossoverFields[high] = frequency;
            void Changed()
            {
                if (syncing || frequency.Value is null || type.SelectedItem is not CrossoverOption option)
                    return;
                var next = new CrossoverFilter((double)frequency.Value.Value, option.Code, active.IsChecked == true);
                if (high)
                {
                    highPass = next;
                    graph.HighPass = next;
                }
                else
                {
                    lowPass = next;
                    graph.LowPass = next;
                }
            }
            type.SelectionChanged += (_, _) => Changed();
            frequency.ValueChanged += (_, _) => Changed();
            active.IsCheckedChanged += (_, _) => Changed();
            resetCrossovers.Add(() =>
            {
                if (filter is null || !active.IsEffectivelyEnabled) return;
                type.SelectedItem = CrossoverFilters.Options.Single(o => o.Code == 0);
                frequency.Value = high ? 20 : 20000;
                active.IsChecked = false;
                Changed();
            });
            var card = Card(column);
            card.Padding = new Thickness(8);
            return card;
        }
        AddColumn(cards, CrossoverCard(true), 0);
        AddColumn(cards, CrossoverCard(false), 9);
        graph.CrossoverChanged += (high, filter) => { syncing = true; if (high) highPass = filter; else lowPass = filter; crossoverFields[high].Value = (decimal)filter.FrequencyHz; syncing = false; };
        graph.BandChanged += band =>
        {
            syncing = true;
            draft[band.Number - 1] = band;
            fields[band.Number].Frequency.Value = (decimal)band.FrequencyHz;
            fields[band.Number].Gain.Value = (decimal)band.GainDb;
            syncing = false;
        };
        enabled.IsCheckedChanged += (_, _) => { graph.EqEnabled = enabled.IsChecked == true; enabled.Content = graph.EqEnabled ? "EQ On" : "EQ Off"; };
        Grid.SetRow(cards, 2);
        content.Children.Add(cards);
        var footer = new StackPanel { Spacing = 10 };
        var message = Text("Drag numbered handles to adjust Hz/dB · 48 kHz preview · Bessel phase-normalized", 11, Muted);
        message.Tag = "eq-help";
        footer.Children.Add(message);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var apply = new Button { Content = "Apply", Tag = "apply-input-eq" };
        var cancel = new Button { Content = "Cancel" };
        var generate = new Button { Content = "Generate FIR…", Tag = "generate-eq-fir" };
        var reset = new Button { Content = "Reset EQ", Tag = "eq-reset" };
        reset.Click += (_, _) =>
        {
            var defaults = PresetOperations.DefaultProcessing(channel);
            foreach (var band in defaults.Bands)
            {
                var controls = fields[band.Number];
                if (!controls.Gain.IsEffectivelyEnabled) continue;
                controls.Frequency.Value = (decimal)band.FrequencyHz;
                controls.Gain.Value = 0;
                controls.Q.Value = (decimal)band.Q;
                controls.Enabled.IsChecked = draft[band.Number-1].FilterType == 0;
            }
            foreach (var restore in resetCrossovers) restore();
            enabled.IsChecked = true;
            message.Text = "Editable filters reset · Apply to save";
        };
        actions.Children.Add(reset);
        actions.Children.Add(generate);
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 3);
        content.Children.Add(footer);
        var canvas = new Viewbox { Child = new Border { Width = 1360, Height = 760, Child = content }, Stretch = Avalonia.Media.Stretch.Uniform };
        var editor = EditorWindow(side+" EQ · Channel " + channel, canvas, 1400, 800);
        editor.MinWidth = 1008;
        editor.MinHeight = 590;
        var screen = Screens.ScreenFromWindow(this);
        if (screen is not null)
        {
            editor.Width = Math.Min(editor.Width, screen.WorkingArea.Width / screen.Scaling - 40);
            editor.Height = Math.Min(editor.Height, screen.WorkingArea.Height / screen.Scaling - 40);
        }
        AddChannelNavigation(actions, editor, editedId, channel, output, n => OpenInputEqEditor(n, output),
            () => input.EqEnabled != (enabled.IsChecked == true) || !input.Bands.SequenceEqual(draft) ||
                highPass != input.HighPass || lowPass != input.LowPass,
            async target =>
            {
                var change = new InputProcessingChange(editedId,target,EqEnabled:enabled.IsChecked==true,
                    Bands:draft.ToArray(),HighPass:highPass,LowPass:lowPass);
                await Task.Run(() =>
                {
                    if (output) service.ApplyOutputProcessing(service.PlanOutputProcessing(change).Id);
                    else service.ApplyInputProcessing(service.PlanInputProcessing(change).Id);
                });
            },message);
        generate.Click += async (_, _) =>
        {
            try
            {
                var taps = EqualizerResponse.GenerateFir(draft, highPass, lowPass, enabled.IsChecked == true);
                var file = await editor.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Save generated FIR", SuggestedFileName = $"{side}-{channel}-EQ.txt",
                    DefaultExtension = "txt"
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync();
                stream.SetLength(0);
                await using var writer = new System.IO.StreamWriter(stream);
                foreach (var tap in taps)
                    await writer.WriteLineAsync(tap.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                message.Text = "FIR saved · 512 taps · 48 kHz";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        apply.Click += async (_, _) =>
        {
            if (fields.Values.Any(f => f.Frequency.Value is null || f.Gain.Value is null || f.Q.Value is null))
            {
                message.Text = "Enter all EQ values.";
                return;
            }
            try
            {
                var change=new InputProcessingChange(editedId,channel,EqEnabled:enabled.IsChecked==true,Bands:draft,HighPass:highPass!=input.HighPass ? highPass : null,LowPass:lowPass!=input.LowPass ? lowPass : null);
                var plan=output ? service.PlanOutputProcessing(change) : service.PlanInputProcessing(change);
                if (plan.Before.EqEnabled != input.EqEnabled || !plan.Before.Bands.SequenceEqual(input.Bands) || plan.Before.HighPass != input.HighPass || plan.Before.LowPass != input.LowPass)
                    throw new InvalidOperationException("EQ changed externally. Reopen the editor before applying.");
                void Apply() { if(output) service.ApplyOutputProcessing(plan.Id); else service.ApplyInputProcessing(plan.Id); }
                if (service.GetDevice(editedId).Readings?.ReadOnly == true) await Task.Run(Apply); else Apply();
                Render();
                editor.Close();
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        cancel.Click += (_, _) => editor.Close();
        ShowInputEditor(editor, editedId, channel, kind);
    }
}
